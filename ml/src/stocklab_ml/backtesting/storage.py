"""Validated CSV/JSON artifacts using the existing per-file atomic writer."""

from dataclasses import asdict
from itertools import combinations
from numbers import Integral, Real
from pathlib import Path

from stocklab_ml.data.models import StorageError
from stocklab_ml.data.storage import check_collisions, save_processed, save_raw
from .contracts import BacktestResult, SavedBacktest
from .validation import validate_result

DEFAULT_BACKTEST_DIR = Path(__file__).resolve().parents[3] / "results" / "backtesting"


def save_backtest_result(result: BacktestResult, *, output_dir: str | Path | None = None,
                         source_paths: tuple[str | Path, ...] = (), overwrite: bool = False) -> SavedBacktest:
    """Validate before writing; no-clobber by default, atomic per file only.

    Pass all source CSV/JSON paths when loading inputs from disk. Outputs and
    sources must be distinct, including symlinks and hard links. A later file
    failure may leave earlier complete artifacts; no four-file transaction.
    """
    validate_result(result)
    payload = asdict(result.report)
    for name, value in payload.items():
        if isinstance(value, Integral):
            payload[name] = int(value)
        elif isinstance(value, Real):
            payload[name] = float(value)
    directory = Path(output_dir) if output_dir is not None else DEFAULT_BACKTEST_DIR
    paths = tuple(directory / name for name in ["trades.csv", "equity_curve.csv", "positions.csv", "report.json"])
    sources = tuple(Path(path) for path in source_paths)
    try:
        pairs = list(combinations(paths, 2)) + [(output, source) for output in paths for source in sources]
        for left, right in pairs:
            if left.resolve() == right.resolve() or (left.exists() and right.exists() and left.samefile(right)):
                raise StorageError("Backtest output and source paths must be distinct.")
    except OSError:
        raise StorageError("Could not resolve local backtest paths.") from None
    check_collisions(paths, overwrite)
    for path, frame in zip(paths[:3], [result.trades, result.equity_curve, result.positions_history], strict=True):
        save_processed(path, frame, overwrite=overwrite)
    save_raw(paths[3], payload, overwrite=overwrite)
    return SavedBacktest(*paths)
