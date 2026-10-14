"""Dedicated no-clobber batch exports; existing unversioned files stay separate."""

from dataclasses import dataclass
from pathlib import Path

from stocklab_ml.data.models import StorageError
from stocklab_ml.data.storage import check_collisions, cleaning_paths, save_processed, save_raw
from stocklab_ml.signals.confidence import _report_metadata
from .association import VersionedConfidenceDecisions, VersionedPredictions
from .contracts import ModelVersionError
from .validation import frame_digest, validate_uuid

DEFAULT_PREDICTIONS_DIR = Path(__file__).resolve().parents[3] / "results" / "versioned_predictions"
DEFAULT_DECISIONS_DIR = DEFAULT_PREDICTIONS_DIR.parent / "versioned_decisions"


@dataclass(frozen=True)
class SavedVersionedOutput:
    data_path: Path
    metadata_path: Path
    batch_id: str


def _save(result, *, batch_id, output_dir, kind, overwrite):
    validate_uuid(batch_id)
    frame = result.to_dataframe()  # Revalidate the immutable reference and all output rows before writing.
    root = Path(output_dir).resolve()
    directory = root / result.record.model_name / result.record.model_version / batch_id
    if not directory.resolve().is_relative_to(root):
        raise StorageError("Versioned output must remain inside the selected local results directory.")
    data, metadata = directory / f"{kind}.csv", directory / "metadata.json"
    for source in (result._registry_path,) + result._source_paths:
        cleaning_paths(Path(source), data, metadata)
    check_collisions((data, metadata), overwrite)
    payload = {
        "schema_version": 1, "kind": kind, "batch_id": batch_id,
        "model_name": result.record.model_name, "model_version": result.record.model_version,
        "training_run_id": result.record.training_run_id, "row_count": len(frame),
        "date_min": frame.date.min().date().isoformat(), "date_max": frame.date.max().date().isoformat(),
        "symbols": sorted(frame.symbol.unique().tolist()), "data_sha256": frame_digest(frame),
        "model_record": result.record.to_dict(),
    }
    if isinstance(result, VersionedConfidenceDecisions):
        payload["decision_report"] = _report_metadata(result.result.report)
    save_processed(data, frame, overwrite=overwrite)
    save_raw(metadata, payload, overwrite=overwrite)
    return SavedVersionedOutput(data, metadata, batch_id)


def save_versioned_predictions(
    result: VersionedPredictions, *, batch_id: str, output_dir: str | Path | None = None,
    overwrite: bool = False,
) -> SavedVersionedOutput:
    """Save predictions.csv + metadata.json; atomic per file, not across the pair."""
    if not isinstance(result, VersionedPredictions):
        raise ModelVersionError("A VersionedPredictions result is required.")
    return _save(result, batch_id=batch_id, output_dir=output_dir if output_dir is not None else DEFAULT_PREDICTIONS_DIR,
                 kind="predictions", overwrite=overwrite)


def save_versioned_decisions(
    result: VersionedConfidenceDecisions, *, batch_id: str, output_dir: str | Path | None = None,
    overwrite: bool = False,
) -> SavedVersionedOutput:
    """Save unchanged decisions with an explicit model version on every row."""
    if not isinstance(result, VersionedConfidenceDecisions):
        raise ModelVersionError("A VersionedConfidenceDecisions result is required.")
    return _save(result, batch_id=batch_id, output_dir=output_dir if output_dir is not None else DEFAULT_DECISIONS_DIR,
                 kind="decisions", overwrite=overwrite)
