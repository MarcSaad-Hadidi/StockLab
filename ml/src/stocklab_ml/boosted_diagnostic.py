"""Explicit Archived HGBT28 diagnostic; never a live/persistable inference batch."""

import argparse
from datetime import UTC, datetime, timedelta
import hashlib
import json
from pathlib import Path
import re
import sys

from stocklab_ml.artifacts import load_model_artifact
from stocklab_ml.artifacts.local_storage import artifact_paths, read_bounded
from stocklab_ml.boosted_snapshot import FEATURES, SYMBOLS, _finite, _keys
from stocklab_ml.boosted_vintage import _ordinary, prepare_vintage
from stocklab_ml.versioning import ModelStatus, ModelVersionRegistry
from stocklab_ml.versioning.validation import frame_digest


class BoostedDiagnosticError(Exception):
    """Sanitized diagnostic failure; no partial batch is available."""


def _digest(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def _utc(value):
    if type(value) is not datetime or value.tzinfo is None or value.utcoffset() != timedelta(0):
        raise ValueError("UTC is required.")
    return value


def _path(value, *, directory=False):
    path = Path(value)
    if not path.is_absolute():
        raise ValueError("An explicit absolute path is required.")
    _ordinary(path, directory=directory)
    return path


def _registry_bytes(path, expected):
    data = read_bounded(path, 8 * 1024 * 1024)
    if hashlib.sha256(data).hexdigest() != expected:
        raise ValueError("Registry bytes changed.")
    return data


def infer_diagnostic(*, registry_path, registry_sha256, artifacts_dir, model_version,
                     bundle_dir, manifest_sha256, as_of_utc, trusted_artifacts=False) -> dict:
    """Return the five latest raw decisions from one explicit verified Archived version.

    A historical source as-of can precede actual model creation only as a marked
    diagnostic. Features must still be on/after its historical fit boundary.
    """
    try:
        if (trusted_artifacts is not True or not _digest(registry_sha256) or not _digest(manifest_sha256)
                or not isinstance(model_version, str) or re.fullmatch(r"v[0-9]{6,}", model_version) is None
                or int(model_version[1:]) < 1 or "v" + str(int(model_version[1:])).zfill(6) != model_version
                or _utc(as_of_utc) > datetime.now(UTC)):
            raise ValueError("Unsupported diagnostic identity.")
        registry_path = _path(registry_path)
        artifacts_dir = _path(artifacts_dir, directory=True)
        bundle_dir = _path(bundle_dir, directory=True)
        before = _registry_bytes(registry_path, registry_sha256)
        registry = ModelVersionRegistry(registry_path)
        record = registry.get_version("hgb_swing20_market28", model_version)
        if record.model_type != "hist_gradient_boosting" or record.status is not ModelStatus.ARCHIVED:
            raise ValueError("The explicit HGBT28 version must remain Archived.")
        metadata = record.to_dict()["training_report"]["snapshot_metadata"]
        if (metadata["target_definition"] != "open[t+21]/open[t+1]-1 > .002"
                or metadata["known_date_offset_sessions"] != 21 or metadata["feature_columns"] != list(FEATURES)):
            raise ValueError("Wrong model profile.")
        features, preparation = prepare_vintage(bundle_dir, manifest_sha256=manifest_sha256, as_of_utc=as_of_utc)
        _keys(features)
        _finite(features, FEATURES)
        if features.columns.tolist() != ["date", "symbol"] + list(FEATURES):
            raise ValueError("Exact target-free columns are required.")
        cutoff = datetime.strptime(preparation["cutoff_date"], "%Y-%m-%d").date()
        boundary = datetime.strptime(metadata["fit_audit"]["first_prediction"], "%Y-%m-%d").date()
        prepared = _utc(datetime.fromisoformat(preparation["prepared_at_utc"]))
        if (cutoff < boundary or cutoff >= as_of_utc.date() or preparation["as_of_utc"] != as_of_utc.isoformat()
                or not as_of_utc <= prepared <= datetime.now(UTC)):
            raise ValueError("Invalid diagnostic timing or backward features.")
        latest = features.loc[features.date.dt.date.eq(cutoff)].reset_index(drop=True)
        if len(latest) != 5 or latest.symbol.tolist() != sorted(SYMBOLS):
            raise ValueError("Exactly the latest original-five rows are required.")
        loaded = load_model_artifact(artifact_paths(record.model_name, model_version, artifacts_dir),
            registry=registry, trusted_source=True)
        if loaded.record != record:
            raise ValueError("Artifact identity differs from the requested record.")
        predictions = loaded.predict(latest)
        decisions = predictions.policy_decisions(latest).decisions
        if (registry.get_version(record.model_name, model_version) != record
                or _registry_bytes(registry_path, registry_sha256) != before):
            raise ValueError("Registry changed during diagnostic inference.")
        context = latest[["date", "symbol", "close_ma50", "close_ma200", "momentum_20"]]
        joined = decisions.merge(context, on=["date", "symbol"], how="left", validate="one_to_one")
        rows = joined[["symbol", "probability_up", "action", "reason", "close_ma50", "close_ma200", "momentum_20"]].to_dict("records")
        inferred = datetime.now(UTC)
        trained = _utc(record.trained_at_utc)
        if trained > inferred:
            raise ValueError("A future actual model timestamp is forbidden.")
        result = {"schema_version": 1, "mode": "diagnostic_only", "execution_eligible": False,
            "model_name": record.model_name, "model_version": model_version, "model_status": "Archived",
            "target_name": "target_20", "target_definition": metadata["target_definition"], "horizon_sessions": 20,
            "policy": "trend200-immediate-v1", "feature_date": cutoff.isoformat(), "as_of_utc": as_of_utc.isoformat(),
            "prepared_at_utc": prepared.isoformat(), "inferred_at_utc": inferred.isoformat(),
            "trained_at_utc": trained.isoformat(), "fit_boundary": boundary.isoformat(),
            "model_created_after_as_of": trained > as_of_utc, "prospective_status": "PENDING",
            "input_manifest_sha256": manifest_sha256, "feature_frame_sha256": frame_digest(latest),
            "policy_context_sha256": frame_digest(context), "artifact_sha256": loaded.artifact_sha256,
            "registry_sha256": registry_sha256, "decisions": rows}
        if len(json.dumps(result, allow_nan=False).encode("utf-8")) > 65536:
            raise ValueError("Diagnostic output exceeds the process boundary.")
        return result
    except Exception:
        raise BoostedDiagnosticError("boosted_diagnostic_failed") from None


class _Parser(argparse.ArgumentParser):
    def error(self, message):
        self.exit(2, "boosted_diagnostic_invalid_arguments\n")


def main(argv=None) -> int:
    parser = _Parser(description=__doc__, allow_abbrev=False)
    for name in ("registry-path", "artifacts-dir", "bundle-dir"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("registry-sha256", "model-version", "manifest-sha256", "as-of-utc"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--trusted-artifacts", action="store_true", required=True)
    args = vars(parser.parse_args(argv))
    try:
        args["as_of_utc"] = datetime.fromisoformat(args["as_of_utc"])
        result = infer_diagnostic(**args)
        print(json.dumps(result, allow_nan=False, separators=(",", ":")))
        return 0
    except (BoostedDiagnosticError, ValueError):
        print("boosted_diagnostic_failed", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
