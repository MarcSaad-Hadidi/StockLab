"""One causal common holdout, with the unchanged #63/#64/#65/#73/#74 APIs."""

from dataclasses import asdict

import pandas as pd

from stocklab_ml.artifacts import (
    ArtifactCompatibilityError, ArtifactIntegrityError, ArtifactNotFoundError, LoadedModel,
    LocalModelArtifact, ModelArtifactError, S3ModelArtifactStore, load_model_artifact,
)
from stocklab_ml.artifacts.local_storage import read_local
from stocklab_ml.artifacts.validation import metadata_digest, runtime_environment
from stocklab_ml.backtesting import compare_to_buy_and_hold, run_backtest
from stocklab_ml.backtesting.contracts import BacktestError
from stocklab_ml.data.models import CleanedDatasetResult, DataCleaningReport
from stocklab_ml.features import FEATURE_COLUMNS, FeatureEngineeringError, engineer_features
from stocklab_ml.modeling import build_supervised_dataset, evaluate_predictions
from stocklab_ml.modeling.contracts import ModelingError, TARGET_NAME
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals
from stocklab_ml.signals.contracts import ConfidenceScoreError, TradingSignalError
from stocklab_ml.versioning import version_confidence_decisions
from stocklab_ml.versioning.validation import frame_digest
from .contracts import ModelPromotionError


class BlockedEvidence(ModelPromotionError):
    def __init__(self, code):
        super().__init__(code)
        self.code = code


def load_exact(source, record, registry, *, trusted_source, cache_dir):
    """A supplied LoadedModel is a reference, never proof of its in-memory bytes."""
    if trusted_source is not True:
        raise BlockedEvidence("UNTRUSTED_SOURCE")
    if source is None:
        raise ArtifactNotFoundError("Exact artifact source is required.")
    limit = {}
    if isinstance(source, S3ModelArtifactStore):
        limit = {"max_artifact_bytes": source.config.max_artifact_bytes}
        source = source.download_model_artifact(record.model_name, record.model_version,
                                                registry=registry, output_dir=cache_dir)
    if isinstance(source, LoadedModel):
        if (source.record.model_name, source.record.model_version) != (record.model_name, record.model_version):
            raise BlockedEvidence("MODEL_VERSION_MISMATCH")
        source = source.artifact_reference
    if not isinstance(source, LocalModelArtifact):
        raise BlockedEvidence("INVALID_ARTIFACT_SOURCE")
    _, supplied, _ = read_local(source, registry=registry, **limit)
    if supplied.model_name != record.model_name:
        raise BlockedEvidence("WRONG_MODEL_FAMILY")
    if supplied.model_version != record.model_version or metadata_digest(supplied) != metadata_digest(record):
        raise BlockedEvidence("MODEL_VERSION_MISMATCH")
    loaded = load_model_artifact(source, registry=registry, trusted_source=True, **limit)
    if metadata_digest(loaded.record) != metadata_digest(record):
        raise BlockedEvidence("REGISTRY_CHANGED")
    return loaded


def load_for_role(source, record, registry, *, role, trusted_source, cache_dir):
    try:
        return load_exact(source, record, registry, trusted_source=trusted_source, cache_dir=cache_dir)
    except ArtifactNotFoundError as error:
        raise BlockedEvidence(f"MISSING_{role.upper()}_ARTIFACT") from error
    except ArtifactCompatibilityError as error:
        raise BlockedEvidence("INCOMPATIBLE_RUNTIME") from error
    except ArtifactIntegrityError as error:
        raise BlockedEvidence("CORRUPTED_ARTIFACT") from error
    except ModelArtifactError as error:
        raise BlockedEvidence("ARTIFACT_STORAGE_ERROR") from error


def market_subset(frame):
    """Describe an already validated slice, without cleaning, filling, or fetching."""
    frame = frame.copy(deep=True).reset_index(drop=True)
    return CleanedDatasetResult(frame, DataCleaningReport(len(frame), len(frame), 0, {},
        frame.date.min().date().isoformat(), frame.date.max().date().isoformat(),
        frame.symbol.unique().tolist(), 1.0))


def common_data(request, records):
    source = request.evaluation_dataset.dataframe.copy(deep=True)
    source = source[source.date <= request.evaluation_end].reset_index(drop=True)
    try:
        features = engineer_features(source).dataframe
        supervised = build_supervised_dataset(features).dataframe
    except (ModelingError, FeatureEngineeringError, ValueError) as error:
        raise BlockedEvidence("INSUFFICIENT_COMMON_DATA") from error
    # #75 validates SPLIT_RULE: both train feature AND train target dates precede split_date.
    boundary = max(record.split_date for record in records)
    rows = supervised[(supervised.date >= max(boundary, request.evaluation_start))
                      & (supervised.target_date <= request.evaluation_end)]
    rows = rows.sort_values(["date", "symbol"]).reset_index(drop=True)
    if rows.empty:
        raise BlockedEvidence("INSUFFICIENT_COMMON_DATA")
    labels = rows[["date", "target_date", "symbol", TARGET_NAME]].rename(columns={TARGET_NAME: "actual_class"})
    inputs = rows[["date", "symbol"] + FEATURE_COLUMNS].copy(deep=True)
    symbols = sorted(rows.symbol.unique().tolist())
    start, end = rows.date.min(), rows.target_date.max()
    market = market_subset(source[source.symbol.isin(symbols) & source.date.between(start, end)])
    # Without exchange close timestamps, same-UTC-day availability is not forward evidence.
    available_day = max(record.trained_at_utc.date() for record in records)
    forward_rows = int((rows.date > pd.Timestamp(available_day)).sum())
    summary = {"source_sha256": frame_digest(request.evaluation_dataset.dataframe),
        "market_data_sha256": frame_digest(market.dataframe), "feature_rows_sha256": frame_digest(inputs),
        "labels_sha256": frame_digest(labels), "evaluation_rows": len(rows), "symbols": symbols,
        "feature_start": start.date().isoformat(), "feature_end": rows.date.max().date().isoformat(),
        "target_end": end.date().isoformat(), "training_boundary": boundary,
        "requested_start": request.evaluation_start, "requested_end": request.evaluation_end,
        "forward_rows": forward_rows,
        "evidence_kind": "historical" if not forward_rows else "prospective" if forward_rows == len(rows) else "mixed",
        "availability_rule": "feature session date strictly after both trained_at_utc dates",
        "backtest_config": asdict(request.backtest_config)}
    return inputs, labels, market, summary


def evaluate_loaded(loaded, inputs, labels, market, config):
    try:
        predictions = loaded.predict(inputs.copy(deep=True))
        frame = predictions.predictions
        if not frame[["date", "symbol"]].equals(inputs[["date", "symbol"]]):
            raise BlockedEvidence("DATASET_MISMATCH")
        historical = labels.copy(deep=True)
        historical["predicted_class"] = frame.predicted_class
        historical["probability_up"] = frame.probability_up
        metrics = evaluate_predictions(model_name=loaded.record.model_name, predictions=historical)
        confidence = add_confidence_scores(generate_trading_signals(frame, model_name=loaded.record.model_name))
        decisions = version_confidence_decisions(confidence, predictions=predictions)
        backtest = run_backtest(decisions.result, market, config=config)
        benchmark = compare_to_buy_and_hold(backtest, market)
    except (ModelingError, BacktestError, ConfidenceScoreError, TradingSignalError) as error:
        raise BlockedEvidence("INVALID_MODEL_EVALUATION") from error
    return {"record": loaded.record.to_dict(), "artifact_sha256": loaded.artifact_sha256,
        "metadata_sha256": metadata_digest(loaded.record), "runtime": runtime_environment(),
        "classification": asdict(metrics), "backtest": asdict(backtest.report),
        "benchmark": asdict(benchmark.benchmark_report), "benchmark_comparison": asdict(benchmark.comparison_report),
        "evaluation_keys_sha256": frame_digest(historical[["date", "target_date", "symbol", "actual_class"]]),
        "predictions_sha256": frame_digest(predictions.to_dataframe()),
        "decisions_sha256": frame_digest(decisions.to_dataframe()),
        "equity_sha256": frame_digest(backtest.equity_curve),
        "equity_dates": [day.date().isoformat() for day in backtest.equity_curve.date],
        "benchmark_equity_sha256": frame_digest(benchmark.benchmark_equity_curve)}
