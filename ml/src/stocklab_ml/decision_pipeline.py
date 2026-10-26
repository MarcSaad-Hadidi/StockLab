"""One offline, Active-model inference cycle; stdout is a bounded backend contract."""

import argparse
from datetime import UTC, date, datetime
from decimal import Decimal
import json
from pathlib import Path
import re
import sys

from stocklab_ml.artifacts import load_model_artifact
from stocklab_ml.artifacts.local_storage import artifact_paths
from stocklab_ml.data.cleaning import clean_historical_dataset
from stocklab_ml.data.storage import load_cleaning_csv
from stocklab_ml.data.validation import SYMBOL_PATTERN, calendar_date
from stocklab_ml.features import engineer_features
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals
from stocklab_ml.versioning import ModelVersionRegistry, version_confidence_decisions


class DecisionInferenceError(Exception):
    """Inference failed; no decision batch is available for persistence."""


def infer_decisions(*, registry_path: Path, artifacts_dir: Path, dataset_path: Path,
                    model_name: str, symbols: tuple[str, ...], decision_date: str,
                    trusted_artifacts: bool = False) -> dict:
    try:
        session = calendar_date(decision_date)
        if (trusted_artifacts is not True or session >= datetime.now(UTC).date()
                or not isinstance(symbols, tuple) or not 1 <= len(symbols) <= 100
                or any(not isinstance(s, str) or len(s) > 32 or not re.fullmatch(SYMBOL_PATTERN, s) for s in symbols)
                or len(set(symbols)) != len(symbols)):
            raise ValueError
        registry = ModelVersionRegistry(registry_path)
        active = registry.get_active_version(model_name)
        if active is None or date.fromisoformat(active.split_date) > session:
            raise ValueError
        loaded = load_model_artifact(artifact_paths(model_name, active.model_version, artifacts_dir),
                                     registry=registry, trusted_source=True)
        cleaned = clean_historical_dataset(load_cleaning_csv(dataset_path), as_of_date=session)
        # Only observations beyond the requested cutoff may be removed, never bad historical input.
        if cleaned.report.rows_removed != cleaned.report.removal_reasons["future_date"]:
            raise ValueError
        frame = cleaned.dataframe[cleaned.dataframe.symbol.isin(symbols)].reset_index(drop=True)
        if set(frame[frame.date.dt.date.eq(session)].symbol) != set(symbols):
            raise ValueError
        features = engineer_features(frame).dataframe
        latest = features[features.date.dt.date.eq(session)].reset_index(drop=True)
        if len(latest) != len(symbols):
            raise ValueError
        predictions = loaded.predict(latest)
        decisions = add_confidence_scores(generate_trading_signals(
            predictions.predictions, model_name=active.model_name))
        bound = version_confidence_decisions(decisions, predictions=predictions).to_dataframe()
        if registry.get_active_version(model_name) != active:
            raise ValueError
        rows = []
        for row in bound.itertuples(index=False):
            confidence = format(Decimal(str(row.confidence)), "f")
            if not re.fullmatch(r"(?:0(?:\.[0-9]{1,28})?|1(?:\.0{1,28})?)", confidence):
                raise ValueError
            rows.append({"symbol": row.symbol, "signal": row.signal, "confidence": confidence})
        return {"schema_version": 1, "model_name": active.model_name,
                "model_version": active.model_version, "decision_date": session.isoformat(), "decisions": rows}
    except Exception:
        # Private artifact/data paths and deserializer/provider details must not cross the process boundary.
        raise DecisionInferenceError("decision_inference_failed") from None


def main() -> int:
    parser = argparse.ArgumentParser(description="Infer original decisions from trusted, verified local inputs.")
    for name in ("registry-path", "artifacts-dir", "dataset-path"):
        parser.add_argument("--" + name, required=True, type=Path)
    parser.add_argument("--model-name", required=True)
    parser.add_argument("--symbols", required=True, nargs="+")
    parser.add_argument("--decision-date", required=True)
    parser.add_argument("--trusted-artifacts", action="store_true")
    args = vars(parser.parse_args())
    args["symbols"] = tuple(args["symbols"])
    try:
        result = infer_decisions(**args)
        print(json.dumps(result, allow_nan=False, separators=(",", ":")))
        return 0
    except DecisionInferenceError:
        print("decision_inference_failed", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
