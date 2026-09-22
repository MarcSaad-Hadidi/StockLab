"""Local feature loading and shared per-file atomic result storage."""

from dataclasses import asdict, replace
from decimal import InvalidOperation
from pathlib import Path
import re

import pandas as pd

from stocklab_ml.data.cleaning import _daily_date, _number
from stocklab_ml.data.models import SchemaValidationError, StorageError
from stocklab_ml.data.storage import (
    check_collisions, cleaning_paths, load_cleaning_csv, save_processed, save_raw,
)
from stocklab_ml.features.models import OUTPUT_COLUMNS
from .contracts import LogisticRegressionResult, ModelDatasetError, RandomForestResult, SavedModelResults
from .dataset import _validate_feature_dataset
from .logistic import train_logistic_regression
from .random_forest import train_random_forest

DEFAULT_RESULTS_DIR = Path(__file__).resolve().parents[3] / "results" / "logistic_regression"
DEFAULT_RANDOM_FOREST_RESULTS_DIR = DEFAULT_RESULTS_DIR.parent / "random_forest"


def load_feature_csv(input_path: str | Path) -> pd.DataFrame:
    """Decode exact #60 serialization without cleaning or recalculating features."""
    try:
        frame = load_cleaning_csv(Path(input_path))
        if list(frame.columns) != OUTPUT_COLUMNS:
            raise ValueError("Expected exact #60 columns")
        if not frame.date.map(lambda value: bool(re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}", value))).all():
            raise ValueError("Expected ISO dates")
        frame["date"] = pd.Series([_daily_date(value) for value in frame.date], dtype="datetime64[ns]")
        frame["symbol"] = frame.symbol.astype("string")
        for column in OUTPUT_COLUMNS:
            if column not in {"date", "symbol", "volume"}:
                frame[column] = pd.Series([_number(value) for value in frame[column]], dtype="float64")
        volumes = [_number(value, volume=True) for value in frame.volume]
        if any(not 0 <= value < 2**63 or value != int(value) for value in volumes):
            raise ValueError("Expected nonnegative Int64 volume")
        frame["volume"] = pd.array([int(value) for value in volumes], dtype="Int64")
        _validate_feature_dataset(frame)
        return frame
    except (SchemaValidationError, ValueError, TypeError, OverflowError, InvalidOperation):
        raise ModelDatasetError("Invalid feature dataset: CSV must follow the exact #60 contract.") from None


def train_logistic_from_feature_file(
    input_path: str | Path, *, test_fraction: float = 0.20,
) -> LogisticRegressionResult:
    """Local convenience API; remember the source for later overwrite protection."""
    source = Path(input_path).resolve()
    result = train_logistic_regression(load_feature_csv(source), test_fraction=test_fraction)
    return replace(result, source_path=source)


def save_model_results(
    result: LogisticRegressionResult | RandomForestResult, *, report_path: str | Path | None = None,
    predictions_path: str | Path | None = None, source_path: str | Path | None = None,
    overwrite: bool = False,
) -> SavedModelResults:
    """Save report + predictions only, using #56's atomic writer.

    Each file is atomic; the pair is not a transaction. A report publication
    failure can leave a complete CSV. Success means both were published.
    DataFrame callers loading their own file should supply source_path; the
    file-training API records it automatically. All known sources are protected.
    """
    directory = DEFAULT_RANDOM_FOREST_RESULTS_DIR if isinstance(result, RandomForestResult) else DEFAULT_RESULTS_DIR
    report = Path(report_path) if report_path is not None else directory / "report.json"
    predictions = Path(predictions_path) if predictions_path is not None else directory / "predictions.csv"
    # Reuse the existing alias/hard-link checks as well as its atomic writer.
    sources = [path for path in (result.source_path, source_path) if path is not None]
    if sources:
        for source in sources:
            cleaning_paths(Path(source), predictions, report)
    else:
        try:
            if (report.resolve() == predictions.resolve()
                    or (report.exists() and predictions.exists() and report.samefile(predictions))):
                raise StorageError("Report and predictions paths must be distinct.")
        except OSError:
            raise StorageError("Could not resolve local result paths.") from None
    check_collisions((predictions, report), overwrite)
    save_processed(predictions, result.predictions, overwrite=overwrite)
    save_raw(report, asdict(result.report), overwrite=overwrite)
    return SavedModelResults(report, predictions)


def train_random_forest_from_feature_file(
    input_path: str | Path, *, test_fraction: float = 0.20,
) -> RandomForestResult:
    """Train and compare from a local #60 file, preserving its source protection."""
    source = Path(input_path).resolve()
    result = train_random_forest(load_feature_csv(source), test_fraction=test_fraction)
    return replace(result, source_path=source)
