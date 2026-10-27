"""Fresh, immutable US-equity inputs for the explicitly launched paper cycle."""
from datetime import date, timedelta
import hashlib
from pathlib import Path
import re
from uuid import uuid4

import pandas as pd

from stocklab_ml.data.cleaning import clean_historical_dataset
from stocklab_ml.data.pipeline import prepare_historical_dataset
from stocklab_ml.data.storage import load_cleaning_csv, save_cleaning_report, save_processed, save_raw
from stocklab_ml.data.validation import SYMBOL_PATTERN, calendar_date
from stocklab_ml.features import engineer_features
from stocklab_ml.retraining.state import read_json, retraining_lock, sync_directory

# shortcut: published regular NYSE/Nasdaq calendars cover 2026–2028; update before 2029.
# https://www.nyse.com/trade/hours-calendars (unexpected closures fail the freshness check).
HOLIDAYS = set("""
2026-01-01 2026-01-19 2026-02-16 2026-04-03 2026-05-25 2026-06-19 2026-07-03 2026-09-07 2026-11-26 2026-12-25
2027-01-01 2027-01-18 2027-02-15 2027-03-26 2027-05-31 2027-06-18 2027-07-05 2027-09-06 2027-11-25 2027-12-24
2028-01-17 2028-02-21 2028-04-14 2028-05-29 2028-06-19 2028-07-04 2028-09-04 2028-11-23 2028-12-25
""".split())


def latest_closed_session(as_of_utc_date: date) -> date:
    if type(as_of_utc_date) is not date or not 2026 <= as_of_utc_date.year <= 2028:
        raise ValueError("Unsupported operational calendar.")
    session = as_of_utc_date - timedelta(days=1)
    while session.weekday() >= 5 or session.isoformat() in HOLIDAYS:
        session -= timedelta(days=1)
    return session


def _fingerprint(path):
    if path.is_symlink() or path.stat().st_size > 64 * 1024 * 1024:
        raise ValueError("Invalid snapshot file.")
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _validated_snapshot(directory, *, session, symbols, history_start, today):
    try:
        manifest = read_json(directory / "manifest.json", limit=65536)
        if (type(manifest) is not dict or set(manifest) != {
                "schema_version", "decision_date", "symbols", "history_start", "sha256", "files"}
                or type(manifest["schema_version"]) is not int or manifest["schema_version"] != 1
                or manifest["decision_date"] != session.isoformat() or manifest["symbols"] != list(symbols)
                or manifest["history_start"] != history_start or type(manifest["files"]) is not dict
                or not 2 <= len(manifest["files"]) <= 32):
            raise ValueError
        for name, digest in manifest["files"].items():
            relative = Path(name)
            path = directory / relative
            if (relative.is_absolute() or ".." in relative.parts
                    or not path.resolve().is_relative_to(directory.resolve())
                    or not isinstance(digest, str) or re.fullmatch(r"[a-f0-9]{64}", digest) is None
                    or _fingerprint(path) != digest):
                raise ValueError
        files = {p.relative_to(directory).as_posix() for p in directory.rglob("*") if p.is_file()}
        if files != set(manifest["files"]) | {"manifest.json"}:
            raise ValueError
        path = directory / "cleaned.csv"
        if manifest["sha256"] != manifest["files"].get("cleaned.csv"):
            raise ValueError
        cleaned = clean_historical_dataset(load_cleaning_csv(path), as_of_date=session)
        frame = cleaned.dataframe
        if (cleaned.report.rows_removed or tuple(sorted(frame.symbol.unique())) != symbols
                or any(group.date.max().date() != session for _, group in frame.groupby("symbol"))):
            raise ValueError
        engineer_features(frame)
        return {"decision_date": session.isoformat(), "as_of_utc_date": today.isoformat(),
                "dataset_path": path, "sha256": manifest["sha256"]}
    except Exception:
        raise ValueError("Invalid operational snapshot; no repair or fallback performed.") from None


def prepare_cycle_dataset(root: Path, *, symbols: tuple[str, ...], history_start: str,
                          as_of_utc_date: date) -> dict:
    session = latest_closed_session(as_of_utc_date)
    if (not isinstance(symbols, tuple) or not 1 <= len(symbols) <= 10
            or any(not isinstance(s, str) or len(s) > 32 or re.fullmatch(SYMBOL_PATTERN, s) is None for s in symbols)
            or len(set(symbols)) != len(symbols) or calendar_date(history_start) >= session):
        raise ValueError("Invalid operational dataset scope.")
    symbols = tuple(sorted(symbols))
    root = Path(root).resolve()
    root.mkdir(parents=True, exist_ok=True)
    directory = root / session.isoformat()
    with retraining_lock(root / "operational-inputs.json"):
        if not directory.exists():
            partial = root / (".partial-" + uuid4().hex)
            partial.mkdir()
            frames = []
            for symbol in symbols:
                result = prepare_historical_dataset(symbol, history_start, session, data_dir=partial)
                if (result.metadata.currency != "USD" or result.metadata.exchange_timezone != "America/New_York"
                        or result.metadata.exchange not in {"NASDAQ", "NYSE", "NYSE ARCA"}
                        or result.end_date != session):
                    raise ValueError("Expected closed US-equity session is unavailable.")
                frames.append(result.dataframe)
            cleaned = clean_historical_dataset(pd.concat(frames, ignore_index=True), as_of_date=session)
            if cleaned.report.rows_removed:
                raise ValueError("Operational data must not require repair.")
            engineer_features(cleaned.dataframe)
            save_processed(partial / "cleaned.csv", cleaned.dataframe)
            save_cleaning_report(partial / "cleaning.json", cleaned.report)
            files = {p.relative_to(partial).as_posix(): _fingerprint(p) for p in partial.rglob("*") if p.is_file()}
            save_raw(partial / "manifest.json", {"schema_version": 1, "decision_date": session.isoformat(),
                "symbols": list(symbols), "history_start": history_start, "sha256": files["cleaned.csv"], "files": files})
            partial.rename(directory)
            sync_directory(root)
        return _validated_snapshot(directory, session=session, symbols=symbols, history_start=history_start,
                                   today=as_of_utc_date)
