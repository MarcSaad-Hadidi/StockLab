"""Validated local benchmark artifacts with existing atomic storage semantics."""

from dataclasses import asdict
from itertools import combinations
import json
from numbers import Integral, Real
from pathlib import Path

from stocklab_ml.data.models import StorageError
from stocklab_ml.data.storage import check_collisions, save_processed, save_raw
from .benchmark_contracts import BenchmarkComparisonResult, SavedBenchmarkComparison
from .benchmark_validation import validate_benchmark_comparison

DEFAULT_BENCHMARK_DIR = Path(__file__).resolve().parents[3] / "results" / "benchmark"


def _plain(value):
    if isinstance(value, dict):
        return {key: _plain(item) for key, item in value.items()}
    if isinstance(value, (tuple, list)):
        return [_plain(item) for item in value]
    if isinstance(value, bool):
        return value
    if isinstance(value, Integral):
        return int(value)
    if isinstance(value, Real):
        return float(value)
    return value


def save_benchmark_comparison(result: BenchmarkComparisonResult, *, output_dir: str | Path | None = None,
                               source_paths: tuple[str | Path, ...] = (),
                               overwrite: bool = False) -> SavedBenchmarkComparison:
    """Validate all accounting before writing. Atomic per file, no transaction.

    Pass all loaded input paths to protect sources, including symlink/hardlink
    aliases. A later file failure may leave earlier complete artifacts.
    """
    validate_benchmark_comparison(result)
    payload = _plain({"benchmark": asdict(result.benchmark_report), "comparison": asdict(result.comparison_report)})
    json.dumps(payload, allow_nan=False)  # Preflight every JSON value before creating outputs.
    directory = Path(output_dir) if output_dir is not None else DEFAULT_BENCHMARK_DIR
    paths = tuple(directory / name for name in ["buy_and_hold_allocations.csv", "buy_and_hold_equity_curve.csv",
                                               "buy_and_hold_positions.csv", "comparison.json"])
    sources = tuple(Path(path) for path in source_paths)
    try:
        for left, right in list(combinations(paths, 2)) + [(out, src) for out in paths for src in sources]:
            if left.resolve() == right.resolve() or (left.exists() and right.exists() and left.samefile(right)):
                raise StorageError("Benchmark outputs and source paths must be distinct.")
    except OSError:
        raise StorageError("Could not resolve local benchmark paths.") from None
    check_collisions(paths, overwrite)
    for path, frame in zip(paths[:3], [result.benchmark_allocations, result.benchmark_equity_curve,
                                     result.benchmark_positions_history], strict=True):
        save_processed(path, frame, overwrite=overwrite)
    save_raw(paths[3], payload, overwrite=overwrite)
    return SavedBenchmarkComparison(*paths)
