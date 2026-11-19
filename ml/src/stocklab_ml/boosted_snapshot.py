"""Explicit offline HGBT28 snapshots, separate from legacy registered/live models."""

import hashlib
from dataclasses import dataclass
from datetime import UTC, datetime
import io
import json
import os
from pathlib import Path
import re
import warnings

import joblib
import numpy as np
import pandas as pd
from sklearn.ensemble import HistGradientBoostingClassifier
from sklearn.exceptions import ConvergenceWarning, InconsistentVersionWarning
from sklearn.metrics import roc_auc_score
from sklearn.utils.validation import check_is_fitted

from stocklab_ml import market_risk_research as market, swing_research as swing
from stocklab_ml.artifacts.contracts import (
    ArtifactCompatibilityError, ArtifactConflictError, ArtifactIntegrityError, ArtifactStorageError,
    DEFAULT_MAX_ARTIFACT_BYTES, MAX_MANIFEST_BYTES, ModelArtifactError,
)
from stocklab_ml.artifacts.local_storage import _atomic_bytes, read_bounded
from stocklab_ml.artifacts.validation import manifest_bytes, runtime_environment, validate_limit, verify_bytes
from stocklab_ml.versioning.validation import frame_digest


FEATURES = tuple(swing.SWING_FEATURES + market.FEATURES)
SYMBOLS = frozenset({"AAPL", "JNJ", "JPM", "MSFT", "XOM"})
TARGET = "target_20"
KNOWN = "target_date_20"
_KEYS = ["date", "symbol"]
_SCORES = _KEYS + ["probability_up"]


def _canonical(value) -> str:
    try:
        return json.dumps(value, sort_keys=True, ensure_ascii=False, allow_nan=False)
    except (TypeError, ValueError, OverflowError) as error:
        raise ValueError("Snapshot metadata must be finite JSON values.") from error


def _digest(value) -> bool:
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def _day(value) -> pd.Timestamp:
    if not isinstance(value, str) or re.fullmatch(r"\d{4}-\d{2}-\d{2}", value) is None:
        raise ValueError("An exact ISO calendar date is required.")
    return pd.Timestamp(value)


def _keys(frame: pd.DataFrame) -> None:
    if (not isinstance(frame, pd.DataFrame) or frame.empty or frame.columns.duplicated().any()
            or not set(_KEYS).issubset(frame) or frame[_KEYS].isna().any().any()
            or frame.date.dtype != np.dtype("datetime64[ns]")
            or not frame.date.eq(frame.date.dt.normalize()).all()
            or not frame.symbol.map(lambda value: isinstance(value, str) and value in SYMBOLS).all()
            or frame.duplicated(_KEYS).any()
            or not frame[_KEYS].reset_index(drop=True).equals(
                frame.sort_values(_KEYS)[_KEYS].reset_index(drop=True))):
        raise ValueError("Unique chronological original-five session keys are required.")


def _finite(frame, columns) -> None:
    if (not set(columns).issubset(frame)
            or any(not pd.api.types.is_numeric_dtype(frame[name]) or pd.api.types.is_bool_dtype(frame[name])
                   or pd.api.types.is_complex_dtype(frame[name])
                   for name in columns)
            or not np.isfinite(frame[list(columns)].to_numpy(dtype="float64")).all()):
        raise ValueError("The exact finite numeric feature/score columns are required.")


def _scores(predictions) -> None:
    _keys(predictions)
    if predictions.columns.tolist() != _SCORES:
        raise ValueError("Exact date/symbol/probability_up predictions are required.")
    _finite(predictions, ["probability_up"])
    if not predictions.probability_up.between(0, 1).all():
        raise ValueError("Model probabilities must be in [0,1].")


def _source_hashes(sources) -> dict:
    if not isinstance(sources, dict) or not sources:
        raise ValueError("Explicit verified historical source hashes are required.")
    result = {}
    for name, digest in sources.items():
        if not isinstance(name, str) or not _digest(digest):
            raise ValueError("Source identities require paths and SHA-256 hashes.")
        path = Path(name)
        if (not path.is_absolute() or ".." in path.parts or path.is_symlink()
                or getattr(path, "is_junction", lambda: False)()):
            raise ValueError("Source identities require ordinary absolute file paths.")
        try:
            actual = hashlib.sha256(read_bounded(path, DEFAULT_MAX_ARTIFACT_BYTES)).hexdigest()
        except ModelArtifactError as error:
            raise ValueError("A bound historical source cannot be verified.") from error
        if actual != digest:
            raise ValueError("A bound historical source changed.")
        result[str(path)] = digest
    return result


def prepare_period(frame: pd.DataFrame, frozen_predictions: pd.DataFrame, frozen_audit: dict, *,
                   refit_frequency: str, frame_sha256: str, prediction_sha256: str,
                   sources: dict) -> tuple[pd.DataFrame, pd.DataFrame, pd.Series, dict]:
    """Verify supplied historical research inputs before any estimator is fitted.

    Source hashes prove byte identity, not contemporaneous past publication.
    Targets inherit the declared research builder's open[t+1] -> open[t+21] contract.
    """
    _keys(frame)
    _finite(frame, FEATURES)
    if (set(frame.symbol) != SYMBOLS or refit_frequency not in ("annual", "monthly")
            or not {TARGET, KNOWN}.issubset(frame)
            or frame[KNOWN].dtype != np.dtype("datetime64[ns]")):
        raise ValueError("The original-five swing20 frame and annual/monthly cadence are required.")
    labels, known = frame[TARGET], frame[KNOWN]
    if (not pd.api.types.is_numeric_dtype(labels) or pd.api.types.is_bool_dtype(labels)
            or pd.api.types.is_complex_dtype(labels)
            or not labels.dropna().isin([0., 1.]).all()
            or not labels.notna().equals(known.notna())
            or not known.dropna().eq(known.dropna().dt.normalize()).all()
            or not known.loc[known.notna()].gt(frame.loc[known.notna(), "date"]).all()):
        raise ValueError("Binary swing20 outcomes require matching later known dates; unknown tails stay unlabeled.")
    if (not _digest(frame_sha256) or frame_digest(frame) != frame_sha256
            or not _digest(prediction_sha256) or frame_digest(frozen_predictions) != prediction_sha256):
        raise ValueError("The exact bound research frame and frozen probability vector are required.")
    bound_sources = _source_hashes(sources)
    _scores(frozen_predictions)
    if (not isinstance(frozen_audit, dict) or not isinstance(frozen_audit.get("recipe"), str)
            or not frozen_audit["recipe"].startswith("boosted_") or len(frozen_audit["recipe"]) > 128):
        raise ValueError("The exact frozen boosted core fit audit is required.")
    first = _day(frozen_audit.get("first_prediction"))
    period_key = str(first.year) if refit_frequency == "annual" else str(first.to_period("M"))
    selected = frame.date.dt.year.eq(first.year) if refit_frequency == "annual" else frame.date.dt.to_period("M").eq(first.to_period("M"))
    rows = frame.loc[selected].copy()
    if (rows.empty or first != rows.date.min()
            or not rows[_KEYS].reset_index(drop=True).equals(frozen_predictions[_KEYS].reset_index(drop=True))):
        raise ValueError("The frozen vector must cover the whole actual observed prediction period.")
    train = frame.loc[(frame.date >= first - pd.DateOffset(years=4)) & (frame.date < first)
        & (known < first) & labels.notna()].copy()
    if train.empty or set(train[TARGET]) != {0., 1.} or set(train.symbol) != SYMBOLS:
        raise ValueError("The purged original-five fit requires both classes.")
    weights = 1. / train.groupby("date").date.transform("size")
    weights = weights / weights.mean()
    prior = float(np.average(train[TARGET], weights=weights))
    audit = {"recipe": frozen_audit["recipe"], "label": TARGET,
        "first_fit": train.date.min().date().isoformat(), "last_fit": train.date.max().date().isoformat(),
        "last_target": train[KNOWN].max().date().isoformat(), "first_prediction": first.date().isoformat(),
        "train_rows": len(train), "training_positive_rate": prior, "date_weighted": True,
        "training_unweighted_positive_rate": float(train[TARGET].mean()), "training_weighted_positive_rate": prior,
        "training_dates": int(train.date.nunique()), "training_identities": int(train.symbol.nunique()),
        "training_rows": len(train)}
    if _canonical(audit) != _canonical(frozen_audit):
        raise ValueError("The frozen core audit differs from the exact population, weights or prior.")
    fit_rows = train[_KEYS + [TARGET, KNOWN]].assign(weight=weights)
    metadata = {"fit_audit": audit, "refit_frequency": refit_frequency, "period_key": period_key,
        "last_prediction": rows.date.max().date().isoformat(), "frame_sha256": frame_sha256,
        "reference_predictions_sha256": prediction_sha256, "fit_rows_sha256": frame_digest(fit_rows),
        "prediction_features_sha256": frame_digest(rows[_KEYS + list(FEATURES)]),
        "sources_sha256": bound_sources}
    return train, rows, weights, metadata


def policy_decisions(predictions: pd.DataFrame, features: pd.DataFrame) -> pd.DataFrame:
    """Immediate trend200 decisions, including truthful trend-blocked HOLD rows.

    Raw probabilities are never overwritten or passed to legacy HOLD/confidence validation.
    """
    _scores(predictions)
    _keys(features)
    columns = ["close_ma50", "close_ma200", "momentum_20"]
    if not set(columns).issubset(features):
        raise ValueError("Every score needs matching trend context.")
    rows = predictions.merge(features[_KEYS + columns], on=_KEYS, how="left", validate="one_to_one")
    _finite(rows, columns)
    high, low = rows.probability_up.ge(.65), rows.probability_up.le(.35)
    buy = high & rows.close_ma50.gt(0)
    sell = low & rows.close_ma200.lt(0) & rows.momentum_20.lt(0)
    rows["action"] = np.select([buy, sell], ["BUY", "SELL"], default="HOLD")
    rows["reason"] = np.select([buy | sell, high, low],
        ["up_model", "buy_trend_failed", "sell_trend_failed"], default="threshold_hold")
    return rows[_SCORES + ["action", "reason"]]


def _estimator():
    return HistGradientBoostingClassifier(max_iter=200, learning_rate=.05,
        max_leaf_nodes=15, l2_regularization=1., early_stopping=False, random_state=42)


_IDENTITY = {"schema_version": 1, "model_name": "hgb_swing20_market28",
    "model_type": "hist_gradient_boosting", "feature_columns": list(FEATURES),
    "target_definition": "open[t+21]/open[t+1]-1 > .002", "known_date_offset_sessions": 21,
    "promotion_authorized": False}
_EVIDENCE = {"recipe": "frozen_period_parity",
    "historical": "known-date purge on supplied current-vintage frame; past publication not certified",
    "prospective": {"status": "PENDING", "metrics": None}}
_PREPARED_FIELDS = {"fit_audit", "refit_frequency", "period_key", "last_prediction", "frame_sha256",
    "reference_predictions_sha256", "fit_rows_sha256", "prediction_features_sha256", "sources_sha256"}
_METADATA_FIELDS = _PREPARED_FIELDS | set(_IDENTITY) | {
    "parameters", "environment", "trained_at_utc", "omp_num_threads", "evidence"}
_AUDIT_FIELDS = {"recipe", "label", "first_fit", "last_fit", "last_target", "first_prediction",
    "train_rows", "training_positive_rate", "date_weighted", "training_unweighted_positive_rate",
    "training_weighted_positive_rate", "training_dates", "training_identities", "training_rows"}


def _metadata(value):
    if (not isinstance(value, dict) or set(value) != _METADATA_FIELDS
            or any(_canonical(value[key]) != _canonical(expected) for key, expected in _IDENTITY.items())
            or value["omp_num_threads"] != "1" or value["evidence"] != _EVIDENCE):
        raise ValueError("Unsupported boosted snapshot identity or evidence attribution.")
    environment, current = value["environment"], runtime_environment()
    if (not isinstance(environment, dict) or set(environment) != set(current)
            or any(not isinstance(v, str) or not v or len(v) > 64 for v in environment.values())
            or re.fullmatch(r"\d+\.\d+\.\d+", environment["python"]) is None):
        raise ValueError("Invalid snapshot runtime metadata.")
    if (environment["python"].split(".")[:2] != current["python"].split(".")[:2]
            or any(environment[key] != current[key] for key in current if key != "python")):
        raise ArtifactCompatibilityError("Snapshot requires its recorded compatible Python and exact packages.")
    if _canonical(value["parameters"]) != _canonical(_estimator().get_params(deep=False)):
        raise ValueError("Snapshot parameters differ from the fixed genuine HGBT recipe.")
    created = datetime.fromisoformat(value["trained_at_utc"])
    if (created.tzinfo is None or created.utcoffset().total_seconds() != 0
            or created.isoformat() != value["trained_at_utc"] or created > datetime.now(UTC)):
        raise ValueError("The actual training timestamp must be canonical UTC and not in the future.")
    for key in ("frame_sha256", "reference_predictions_sha256", "fit_rows_sha256", "prediction_features_sha256"):
        if not _digest(value[key]):
            raise ValueError("Every snapshot input/population binding requires SHA-256.")
    sources = value["sources_sha256"]
    if (not isinstance(sources, dict) or not sources
            or any(not isinstance(k, str) or not k or len(k) > 4096 or not _digest(v) for k, v in sources.items())):
        raise ValueError("Snapshot source bindings are malformed.")
    audit = value["fit_audit"]
    if (not isinstance(audit, dict) or set(audit) != _AUDIT_FIELDS
            or not isinstance(audit["recipe"], str) or not audit["recipe"].startswith("boosted_")
            or len(audit["recipe"]) > 128 or audit["label"] != TARGET or audit["date_weighted"] is not True):
        raise ValueError("An exact raw boosted20 core audit is required.")
    for key in ("train_rows", "training_rows", "training_dates", "training_identities"):
        if type(audit[key]) is not int or audit[key] < 1:
            raise ValueError("Fit counts must be positive integers.")
    if (audit["train_rows"] != audit["training_rows"] or audit["training_identities"] != 5
            or audit["train_rows"] < audit["training_identities"]
            or not audit["training_dates"] <= audit["train_rows"] <= audit["training_dates"] * 5):
        raise ValueError("The fit counts must describe original-five date-weighted history.")
    for key in ("training_positive_rate", "training_unweighted_positive_rate", "training_weighted_positive_rate"):
        rate = audit[key]
        if type(rate) not in (int, float) or not np.isfinite(rate) or not 0 < rate < 1:
            raise ValueError("Both-class training priors must be finite interior rates.")
    if audit["training_positive_rate"] != audit["training_weighted_positive_rate"]:
        raise ValueError("The actual weighted training prior must remain identical.")
    first, last = _day(audit["first_prediction"]), _day(value["last_prediction"])
    start, end, known = (_day(audit[k]) for k in ("first_fit", "last_fit", "last_target"))
    if created.date() < known.date():
        raise ValueError("Actual training creation cannot precede a consumed known outcome.")
    cadence = value["refit_frequency"]
    if (cadence not in ("annual", "monthly") or not first - pd.DateOffset(years=4) <= start <= end < known < first <= last
            or value["period_key"] != (str(first.year) if cadence == "annual" else str(first.to_period("M")))
            or (last.year != first.year if cadence == "annual" else last.to_period("M") != first.to_period("M"))):
        raise ValueError("The historical fit/known/prediction boundaries or cadence are inconsistent.")


def _model(model, metadata):
    if type(model) is not HistGradientBoostingClassifier:
        raise ValueError("A genuine fitted HistGradientBoostingClassifier is required.")
    check_is_fitted(model)
    if (model.n_features_in_ != 28 or tuple(model.feature_names_in_) != FEATURES
            or not np.array_equal(model.classes_, [0, 1]) or model.n_iter_ != 200
            or len(model._predictors) != 200
            or _canonical(model.get_params(deep=False)) != _canonical(metadata["parameters"])):
        raise ValueError("The fitted HGBT feature/class/iteration/parameter contract differs.")


def _serialized(model):
    try:
        stream = io.BytesIO()
        joblib.dump(model, stream, protocol=5, compress=0)
        return stream.getvalue()
    except Exception as error:
        raise ArtifactStorageError("The fitted snapshot cannot be serialized.") from error


@dataclass(frozen=True, init=False)
class BoostedSnapshot:
    """A retained estimator with immutable provenance; not a registered/Active model."""

    _model: object
    _metadata_json: str
    _model_bytes: bytes
    _state_sha256: str

    @property
    def metadata(self) -> dict:
        return json.loads(self._metadata_json)

    def _validate(self):
        metadata = self.metadata
        _metadata(metadata)
        _model(self._model, metadata)
        if hashlib.sha256(_serialized(self._model)).hexdigest() != self._state_sha256:
            raise ValueError("The retained estimator was modified after verification.")

    def predict(self, frame: pd.DataFrame) -> pd.DataFrame:
        """Pure target-free probabilities; no fit, provider, policy or registry writes."""
        self._validate()
        _keys(frame)
        _finite(frame, FEATURES)
        probabilities = self._model.predict_proba(frame[list(FEATURES)])
        if probabilities.shape != (len(frame), 2):
            raise ValueError("The estimator must return its two genuine class probabilities.")
        result = frame[_KEYS].copy()
        result["probability_up"] = probabilities[:, 1]
        _scores(result)
        return result.reset_index(drop=True)


def _snapshot(model, metadata, data=None):
    _metadata(metadata)
    _model(model, metadata)
    serialized = _serialized(model)
    result = object.__new__(BoostedSnapshot)
    for key, value in {"_model": model, "_metadata_json": _canonical(metadata),
            "_model_bytes": serialized if data is None else data,
            "_state_sha256": hashlib.sha256(serialized).hexdigest()}.items():
        object.__setattr__(result, key, value)
    return result


def reproduce_snapshot(frame: pd.DataFrame, frozen_predictions: pd.DataFrame, frozen_audit: dict, *,
                       refit_frequency: str, frame_sha256: str, prediction_sha256: str,
                       sources: dict) -> BoostedSnapshot:
    """Fit ONE frozen historical period and require exact whole-period score parity.

    This performs real fitting when explicitly called. No caller-supplied timestamp,
    midperiod refit, tolerance, calibration, artifact export or promotion is accepted.
    """
    train, rows, weights, prepared = prepare_period(frame, frozen_predictions, frozen_audit,
        refit_frequency=refit_frequency, frame_sha256=frame_sha256,
        prediction_sha256=prediction_sha256, sources=sources)
    if os.environ.get("OMP_NUM_THREADS") != "1":
        raise ValueError("Explicit OMP_NUM_THREADS=1 is required for frozen reproduction.")
    model = _estimator()
    with warnings.catch_warnings():
        warnings.simplefilter("error", ConvergenceWarning)
        if weights.nunique() == 1:
            model.fit(train[list(FEATURES)], train[TARGET].astype("int64"))
        else:
            model.fit(train[list(FEATURES)], train[TARGET].astype("int64"), sample_weight=weights)
    actual = model.predict_proba(rows[list(FEATURES)])[:, list(model.classes_).index(1)]
    if not np.array_equal(actual, frozen_predictions.probability_up.to_numpy()):
        raise ValueError("The whole frozen period prediction vector differs; export is blocked.")
    metadata = prepared | _IDENTITY | {"parameters": model.get_params(deep=False),
        "environment": runtime_environment(), "trained_at_utc": datetime.now(UTC).isoformat(),
        "omp_num_threads": "1", "evidence": _EVIDENCE}
    return _snapshot(model, metadata)


def assess_snapshot(snapshot: BoostedSnapshot, frame: pd.DataFrame, *, as_of: str,
                    evidence_kind: str) -> dict:
    """Assess this estimator on its source-bound historical frame, never recipe scores.

    New prospective input/vintage acquisition is deliberately outside this capability.
    A freshly reproduced estimator's prospective assessment therefore remains PENDING.
    """
    snapshot._validate()
    cutoff = _day(as_of)
    metadata = snapshot.metadata
    if (evidence_kind not in ("historical_snapshot", "prospective_snapshot")
            or cutoff.date() > datetime.now(UTC).date() or frame_digest(frame) != metadata["frame_sha256"]):
        raise ValueError("An explicit snapshot evidence kind, actual cutoff and original bound frame are required.")
    first = _day(metadata["fit_audit"]["first_prediction"])
    mask = frame.date.ge(first) & frame[KNOWN].le(cutoff) & frame[TARGET].notna()
    if evidence_kind == "prospective_snapshot":
        mask &= frame.date.dt.date.gt(datetime.fromisoformat(metadata["trained_at_utc"]).date())
    rows = frame.loc[mask].copy()
    dates = [day for day, group in rows.groupby("date", sort=True)
             if len(group) == 5 and set(group.symbol) == SYMBOLS]
    result = {"evidence_kind": evidence_kind, "as_of": as_of, "required_dates": 50,
        "available_dates": len(dates), "dates": 0, "rows": 0, "status": "PENDING", "metrics": None,
        "snapshot_metadata_sha256": hashlib.sha256(snapshot._metadata_json.encode()).hexdigest(),
        "frame_sha256": metadata["frame_sha256"]}
    if len(dates) < 50:
        return result
    rows = rows.loc[rows.date.isin(dates[-50:])]
    predictions = snapshot.predict(rows)
    probabilities = predictions.probability_up.to_numpy()
    labels = rows[TARGET].to_numpy()
    prior = metadata["fit_audit"]["training_positive_rate"]
    result.update(status="AVAILABLE", dates=50, rows=len(rows),
        predictions_sha256=frame_digest(predictions),
        metrics={"brier": float(np.mean((probabilities - labels) ** 2)),
            "training_prior_brier": float(np.mean((prior - labels) ** 2)),
            "auc": float(roc_auc_score(labels, probabilities)) if len(set(labels)) == 2 else None})
    return result


@dataclass(frozen=True)
class SnapshotArtifact:
    """The manifest hash is an external immutable reference, not source authentication."""

    directory: Path
    manifest_sha256: str


def _directory(directory):
    path = Path(directory)
    if ".." in path.parts or any(p.is_symlink() or getattr(p, "is_junction", lambda: False)()
                                  for p in (path, *path.parents)):
        raise ArtifactIntegrityError("Snapshot paths cannot traverse links or parent components.")
    return path.resolve()


def save_snapshot(snapshot: BoostedSnapshot, directory: Path, *,
                  max_artifact_bytes: int = DEFAULT_MAX_ARTIFACT_BYTES) -> SnapshotArtifact:
    """Publish a new immutable pair, manifest last, without registry/default changes."""
    validate_limit(max_artifact_bytes)
    directory = _directory(directory)
    if directory.exists():
        raise ArtifactConflictError("Snapshot output already exists; choose a new directory.")
    if type(snapshot) is not BoostedSnapshot:
        raise ModelArtifactError("A verified retained boosted snapshot is required.")
    try:
        snapshot._validate()
    except (ValueError, TypeError, AttributeError) as error:
        raise ArtifactIntegrityError("The retained snapshot is invalid.") from error
    data = snapshot._model_bytes
    if not 0 < len(data) <= max_artifact_bytes:
        raise ArtifactIntegrityError("Snapshot bytes exceed the configured size limit.")
    manifest = {"schema_version": 1, "serialization_format": "joblib", "artifact_filename": "model.joblib",
        "artifact_sha256": hashlib.sha256(data).hexdigest(), "artifact_size_bytes": len(data),
        "metadata": snapshot.metadata, "metadata_sha256": hashlib.sha256(snapshot._metadata_json.encode()).hexdigest()}
    encoded = manifest_bytes(manifest)
    if len(encoded) > MAX_MANIFEST_BYTES:
        raise ArtifactIntegrityError("Snapshot metadata exceeds the manifest size limit.")
    try:
        directory.mkdir(parents=True, exist_ok=False)
        _atomic_bytes(directory / "model.joblib", data)
        _atomic_bytes(directory / "snapshot.json", encoded)
    except FileExistsError as error:
        raise ArtifactConflictError("Snapshot output appeared concurrently.") from error
    except OSError as error:
        raise ArtifactStorageError("Snapshot publication failed; inspect the incomplete output.") from error
    return SnapshotArtifact(directory, hashlib.sha256(encoded).hexdigest())


def _unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate snapshot metadata key.")
        result[key] = value
    return result


def load_snapshot(artifact: SnapshotArtifact, *, trusted_source: bool = False,
                  max_artifact_bytes: int = DEFAULT_MAX_ARTIFACT_BYTES) -> BoostedSnapshot:
    """Verify bounded metadata/bytes/runtime before explicitly trusted deserialization."""
    if trusted_source is not True:
        raise ModelArtifactError("Explicitly trust the private executable snapshot source.")
    validate_limit(max_artifact_bytes)
    if type(artifact) is not SnapshotArtifact or not _digest(artifact.manifest_sha256):
        raise ArtifactIntegrityError("An exact snapshot manifest reference is required.")
    directory = _directory(artifact.directory)
    try:
        for path in (directory / "snapshot.json", directory / "model.joblib"):
            if path.is_symlink() or path.stat().st_nlink != 1:
                raise ArtifactIntegrityError("Snapshot files must be ordinary private files without links.")
        encoded = read_bounded(directory / "snapshot.json", MAX_MANIFEST_BYTES)
        if hashlib.sha256(encoded).hexdigest() != artifact.manifest_sha256:
            raise ValueError("The immutable snapshot manifest changed.")
        manifest = json.loads(encoded.decode("utf-8"), object_pairs_hook=_unique,
            parse_constant=lambda _: (_ for _ in ()).throw(ValueError("Nonfinite JSON value.")))
        if (not isinstance(manifest, dict) or set(manifest) != {"schema_version", "serialization_format",
                "artifact_filename", "artifact_sha256", "artifact_size_bytes", "metadata", "metadata_sha256"}
                or type(manifest["schema_version"]) is not int or manifest["schema_version"] != 1
                or manifest["serialization_format"] != "joblib" or manifest["artifact_filename"] != "model.joblib"
                or not _digest(manifest["artifact_sha256"]) or not _digest(manifest["metadata_sha256"])
                or type(manifest["artifact_size_bytes"]) is not int
                or not 0 < manifest["artifact_size_bytes"] <= max_artifact_bytes
                or hashlib.sha256(_canonical(manifest["metadata"]).encode()).hexdigest() != manifest["metadata_sha256"]):
            raise ValueError("Unsupported snapshot manifest or metadata digest.")
        _metadata(manifest["metadata"])
        data = read_bounded(directory / "model.joblib", max_artifact_bytes)
        verify_bytes(data, manifest)
    except ModelArtifactError:
        raise
    except (OSError, ValueError, TypeError, KeyError, AttributeError, OverflowError, RecursionError) as error:
        raise ArtifactIntegrityError("Snapshot metadata or immutable bytes are invalid.") from error
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("error", InconsistentVersionWarning)
            model = joblib.load(io.BytesIO(data))
        return _snapshot(model, manifest["metadata"], data)
    except InconsistentVersionWarning as error:
        raise ArtifactCompatibilityError("Snapshot estimator reports an incompatible sklearn runtime.") from error
    except Exception as error:
        raise ArtifactIntegrityError("Verified snapshot bytes do not contain the declared fitted HGBT.") from error
