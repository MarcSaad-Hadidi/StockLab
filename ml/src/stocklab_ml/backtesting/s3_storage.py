"""Exact four-file backtest recovery requires no model or engine execution."""

from stocklab_ml.artifacts.s3_files import S3FileBundleStore, safe_segment
from .contracts import SavedBacktest

BACKTEST_FILES = ("trades.csv", "equity_curve.csv", "positions.csv", "report.json")


class S3BacktestStore(S3FileBundleStore):
    def upload_backtest(self, saved: SavedBacktest, *, backtest_id, metadata=None):
        safe_segment(backtest_id)
        if not isinstance(saved, SavedBacktest):
            raise ValueError("A SavedBacktest from the existing local storage contract is required.")
        paths = dict(zip(BACKTEST_FILES, [saved.trades_path, saved.equity_path,
                                         saved.positions_path, saved.report_path], strict=True))
        return self._upload_bundle(f"backtests/{backtest_id}",
                                   {"type": "backtest", "backtest_id": backtest_id}, paths, metadata)

    def download_backtest(self, backtest_id, *, output_dir):
        safe_segment(backtest_id)
        return self._download_bundle(f"backtests/{backtest_id}",
            {"type": "backtest", "backtest_id": backtest_id}, output_dir, BACKTEST_FILES)
