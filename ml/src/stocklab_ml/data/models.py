"""Data contracts and safe, caller-visible errors."""

from dataclasses import dataclass
from datetime import date
from pathlib import Path

import pandas as pd


class HistoricalDataError(Exception):
    """Base error for historical ingestion."""


class ConfigurationError(HistoricalDataError):
    """Missing or invalid ML configuration."""


class ProviderAuthenticationError(HistoricalDataError):
    """Provider rejected authentication or access."""


class ProviderRateLimitError(HistoricalDataError):
    """Provider credit or rate limit reached."""


class ProviderUnavailableError(HistoricalDataError):
    """Transport or provider service failed."""


class InvalidProviderResponseError(HistoricalDataError):
    """Provider payload does not match the requested contract."""


class DatasetValidationError(HistoricalDataError):
    """Invalid request or OHLCV data; nothing is repaired."""


class RangeTooLargeError(HistoricalDataError):
    """Completeness cannot be trusted within one request."""


class StorageError(HistoricalDataError):
    """Local persistence failed or would overwrite an existing file."""


@dataclass(frozen=True)
class HistoricalRequest:
    symbol: str
    start_date: date
    end_date: date

    @property
    def params(self) -> dict[str, str]:
        symbol, _, exchange = self.symbol.partition(":")
        params = {
            "symbol": symbol, "interval": "1day",
            "start_date": self.start_date.isoformat(),
            "end_date": self.end_date.isoformat(),
            "order": "asc", "format": "JSON", "adjust": "splits",
        }
        if exchange:
            params["exchange"] = exchange
        return params


@dataclass(frozen=True)
class HistoricalBar:
    date: date
    open: float
    high: float
    low: float
    close: float
    volume: int


@dataclass(frozen=True)
class HistoricalMetadata:
    symbol: str
    interval: str
    currency: str
    exchange: str
    mic_code: str | None
    exchange_timezone: str
    name: str | None = None


@dataclass(frozen=True)
class HistoricalDatasetResult:
    symbol: str
    rows: int
    start_date: date  # actual first returned session, not requested boundary
    end_date: date
    raw_path: Path
    processed_path: Path
    metadata: HistoricalMetadata
    dataframe: pd.DataFrame
