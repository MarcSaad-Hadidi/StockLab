"""Raw/cleaned dataset bundles organized by symbol, period, type and dataset id."""

from pathlib import Path
import re
from urllib.parse import quote

from stocklab_ml.artifacts.s3_files import S3FileBundleStore, safe_segment
from .validation import SYMBOL_PATTERN, calendar_date


class S3DatasetStore(S3FileBundleStore):
    def _identity(self, symbol, start_date, end_date, kind, dataset_id):
        if not isinstance(symbol, str) or re.fullmatch(SYMBOL_PATTERN, symbol) is None:
            raise ValueError("An explicit canonical dataset symbol is required.")
        start, end = calendar_date(start_date), calendar_date(end_date)
        if start > end or kind not in {"raw", "cleaned"}:
            raise ValueError("Dataset period must be ordered and kind must be raw or cleaned.")
        safe_segment(dataset_id)
        identity = {"type": "dataset", "symbol": symbol, "start_date": start.isoformat(),
                    "end_date": end.isoformat(), "kind": kind, "dataset_id": dataset_id}
        relative = f"datasets/{quote(symbol, safe='')}/{start}_{end}/{kind}/{dataset_id}"
        return relative, identity

    def upload_dataset(self, path, *, symbol, start_date, end_date, kind, dataset_id, metadata=None):
        relative, identity = self._identity(symbol, start_date, end_date, kind, dataset_id)
        path = Path(path)
        safe_segment(path.name)
        return self._upload_bundle(relative, identity, {path.name: path}, metadata)

    def download_dataset(self, *, symbol, start_date, end_date, kind, dataset_id, output_dir):
        relative, identity = self._identity(symbol, start_date, end_date, kind, dataset_id)
        bundle = self._download_bundle(relative, identity, output_dir)
        return bundle
