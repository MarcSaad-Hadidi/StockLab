"""Deterministic local paths and atomic publication of individual files."""

import json
import os
from pathlib import Path
import tempfile
from urllib.parse import quote

import pandas as pd

from .models import HistoricalRequest, StorageError

DEFAULT_DATA_DIR = Path(__file__).resolve().parents[3] / "data"


def output_paths(request: HistoricalRequest, data_dir: Path) -> tuple[Path, Path]:
    name = f"{quote(request.symbol, safe='')}_1day_{request.start_date}_{request.end_date}"
    return data_dir / "raw" / f"{name}.json", data_dir / "processed" / f"{name}.csv"


def check_collisions(paths: tuple[Path, Path], overwrite: bool) -> None:
    if not overwrite and any(os.path.lexists(path) for path in paths):
        raise StorageError("Dataset output already exists; explicitly set overwrite=True to replace it.")


def _atomic_write(path: Path, content: str, overwrite: bool) -> None:
    temporary = None
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", newline="",
                                         dir=path.parent, prefix=".historical-", suffix=".tmp",
                                         delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        if overwrite:
            os.replace(temporary, path)
        else:
            # Atomic no-clobber publication, including concurrent writers, on
            # NTFS/POSIX. Unsupported filesystems fail safely without fallback.
            os.link(temporary, path)
    except OSError:
        raise StorageError("Could not save dataset atomically; check permissions, filesystem, and collisions.") from None
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def save_raw(path: Path, payload: dict, *, overwrite: bool = False) -> None:
    _atomic_write(path, json.dumps(payload, indent=2, ensure_ascii=False, allow_nan=False) + "\n", overwrite)


def save_processed(path: Path, frame: pd.DataFrame, *, overwrite: bool = False) -> None:
    _atomic_write(path, frame.to_csv(index=False, date_format="%Y-%m-%d", lineterminator="\n"), overwrite)
