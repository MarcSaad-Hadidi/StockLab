"""Synthetic, offline examples of the cleaning contract (no market data)."""

from datetime import date, datetime, timezone
from decimal import Decimal

import numpy as np
import pandas as pd
import pytest

from stocklab_ml.data import cleaning
from stocklab_ml.data.models import DataCleaningError, DuplicateConflictError, SchemaValidationError
from stocklab_ml.data.validation import COLUMNS

AS_OF = "2024-01-31"


def bar(**changes):
    return {"date": "2024-01-05", "symbol": "AAPL", "open": 10,
            "high": 12, "low": 9, "close": 11, "volume": 1000, **changes}


def clean(rows, **kwargs):
    return cleaning.clean_historical_dataset(pd.DataFrame(rows, dtype=object),
                                             as_of_date=AS_OF, **kwargs)


def test_normalization_schema_immutability_determinism_and_idempotence(monkeypatch):
    monkeypatch.delenv("TWELVE_DATA_ML_API_KEY")
    original = pd.DataFrame([
        bar(date="2024-01-08", symbol=" tsla:nasdaq ", volume="1000.0"),
        bar(symbol=" aapl ", open="10", high="12", low="9", close="11"),
        bar(symbol="AAPL ", date=date(2024, 1, 5)),
        bar(symbol="brk.b", volume=0),
        bar(date="2024-01-08", close=None),
    ], dtype=object).loc[:, list(reversed(COLUMNS))]
    original.index = [7, 7, 42, 0, 0]
    before = original.copy(deep=True)
    result = cleaning.clean_historical_dataset(original, as_of_date=AS_OF)
    pd.testing.assert_frame_equal(original, before)
    frame = result.dataframe
    assert list(frame.columns) == COLUMNS
    assert list(frame.index) == [0, 1, 2]
    assert frame.symbol.tolist() == ["AAPL", "BRK.B", "TSLA:NASDAQ"]
    assert str(frame.symbol.dtype) == "string"
    assert str(frame.date.dtype) == "datetime64[ns]"
    assert all(str(frame[c].dtype) == "float64" for c in COLUMNS[2:6])
    assert str(frame.volume.dtype) == "Int64"
    assert frame.volume.tolist() == [1000, 0, 1000]
    report = result.report
    assert (report.input_rows, report.output_rows, report.rows_removed) == (5, 3, 2)
    assert report.removal_reasons["missing_value"] == 1
    assert report.removal_reasons["duplicate_exact_removed"] == 1
    assert sum(report.removal_reasons.values()) == report.rows_removed
    assert report.symbols == ["AAPL", "BRK.B", "TSLA:NASDAQ"]
    assert (report.date_min, report.date_max) == ("2024-01-05", "2024-01-08")
    assert report.retention_ratio == 3 / 5
    again = cleaning.clean_historical_dataset(original, as_of_date=AS_OF)
    pd.testing.assert_frame_equal(frame, again.dataframe)
    assert again.report == report
    shuffled = cleaning.clean_historical_dataset(original.iloc[::-1], as_of_date=AS_OF)
    pd.testing.assert_frame_equal(frame, shuffled.dataframe)
    assert shuffled.report == report
    second = cleaning.clean_historical_dataset(frame, as_of_date=AS_OF)
    pd.testing.assert_frame_equal(frame, second.dataframe)
    assert second.report.rows_removed == 0
    assert second.report.input_rows == second.report.output_rows == 3


@pytest.mark.parametrize("column", COLUMNS)
@pytest.mark.parametrize("value", [None, pd.NA, np.nan, "", "  ", Decimal("sNaN")])
def test_missing_cells_are_dropped_and_counted_once(column, value):
    result = clean([bar(), bar(date="2024-01-08", **{column: value})]
                   if column != "date" else [bar(), bar(date=value)])
    assert len(result.dataframe) == 1
    assert result.report.removal_reasons["missing_value"] == 1
    assert result.report.rows_removed == sum(result.report.removal_reasons.values()) == 1


@pytest.mark.parametrize("column,value,reason", [
    ("symbol", 123, "invalid_symbol"), ("symbol", "A A", "invalid_symbol"),
    ("symbol", "AAPL?apikey=synthetic", "invalid_symbol"),
    ("date", "2024-02-30", "invalid_date"), ("date", "01/05/2024", "invalid_date"),
    ("date", 1704412800000000000, "invalid_date"),
    ("date", "2024-01-05T16:00:00", "invalid_date"),
    ("date", "2024-01-05T00:00:00Z", "invalid_date"),
    ("date", "2024-01-05T00:00:00-05:00", "invalid_date"),
    ("date", datetime(2024, 1, 5, tzinfo=timezone.utc), "invalid_date"),
    ("date", pd.Timestamp("2024-01-05T00:00:00.000000001"), "invalid_date"),
    ("date", "1500-01-01", "invalid_date"), ("date", "2500-01-01", "invalid_date"),
    ("date", "2024-02-01", "future_date"),
    ("open", "abc", "invalid_numeric"), ("close", "NaN", "invalid_numeric"),
    ("open", True, "invalid_numeric"), ("volume", True, "invalid_numeric"),
    ("close", float("inf"), "invalid_numeric"), ("low", "-Infinity", "invalid_numeric"),
    ("volume", "abc", "invalid_numeric"), ("volume", np.inf, "invalid_numeric"),
    ("volume", "-inf", "invalid_numeric"), ("volume", "NaN", "invalid_numeric"),
    ("open", 0, "invalid_price"), ("close", -1, "invalid_price"),
    ("high", 9, "invalid_ohlc"), ("low", 11.5, "invalid_ohlc"),
    ("volume", -1, "invalid_volume"), ("volume", 10.5, "invalid_volume"),
    ("volume", str(2**63), "invalid_volume"),
    ("volume", "1000.00000000000000001", "invalid_volume"),
    ("volume", float(2**63), "invalid_volume"),
    ("volume", np.float64(2**63), "invalid_volume"),
    ("volume", "1e1000000", "invalid_volume"),
])
def test_invalid_cells_have_explicit_reason(column, value, reason):
    result = clean([bar(), {**bar(date="2024-01-08"), column: value}])
    assert result.report.removal_reasons[reason] == 1
    assert result.report.rows_removed == sum(result.report.removal_reasons.values()) == 1
    assert result.dataframe.date.dt.strftime("%Y-%m-%d").tolist() == ["2024-01-05"]


@pytest.mark.parametrize("value", ["2024-01-05", " 2024-01-05 ",
    "2024-01-05 00:00:00", "2024-01-05T00:00:00.000000000",
    date(2024, 1, 5), datetime(2024, 1, 5), pd.Timestamp("2024-01-05"),
    np.datetime64("2024-01-05")])
def test_daily_representations(value):
    result = clean([bar(date=value)])
    assert result.dataframe.date.iloc[0] == pd.Timestamp("2024-01-05")


@pytest.mark.parametrize("value,expected", [(1000, 1000), (1000.0, 1000),
    ("1000.0", 1000), ("1e3", 1000), (Decimal("1000"), 1000),
    (np.int64(1000), 1000), (str(2**63 - 1), 2**63 - 1),
    (float(2**60), 2**60), (np.float32(100000008), 100000008),
    (str(2**53 + 1), 2**53 + 1), (0, 0)])
def test_volume_remains_exact_without_float_roundtrip(value, expected):
    assert clean([bar(volume=value)]).dataframe.volume.iloc[0] == expected


def test_counts_priority_and_invalid_future_rows_before_duplicate_conflicts():
    result = clean([
        bar(), bar(symbol=" aapl ", open="10", volume="1000.0"),
        bar(close=None, volume=-1), bar(date="bad", symbol="invalid symbol"),
        bar(symbol="invalid symbol"), bar(open="abc", volume=-1),
        bar(open=0, volume=-1), bar(high=9, volume=-1), bar(volume=-1),
        bar(date="2024-02-02"), bar(date="2024-02-02", close=12),
    ])
    assert result.report.removal_reasons == {
        "missing_value": 1, "invalid_date": 1, "invalid_symbol": 1,
        "invalid_numeric": 1, "invalid_price": 1, "invalid_ohlc": 1,
        "invalid_volume": 1, "future_date": 2, "duplicate_exact_removed": 1,
    }
    assert (result.report.input_rows, result.report.output_rows, result.report.rows_removed) == (11, 1, 10)


@pytest.mark.parametrize("changes", [{"open": 10.5}, {"high": 13}, {"low": 8},
                                     {"close": 12}, {"volume": 1001}])
def test_conflicting_duplicates_fail_after_normalization(changes):
    with pytest.raises(DuplicateConflictError, match="Conflicting"):
        clean([bar(), bar(symbol=" aapl ", date=datetime(2024, 1, 5), **changes)])


@pytest.mark.parametrize("column", COLUMNS)
def test_missing_column_fails(column):
    with pytest.raises(SchemaValidationError):
        cleaning.clean_historical_dataset(pd.DataFrame([bar()]).drop(columns=column), as_of_date=AS_OF)


@pytest.mark.parametrize("column", ["future_close", "next_return", "target", "label", "tomorrow_price", "index"])
def test_unexpected_column_fails(column):
    with pytest.raises(SchemaValidationError):
        clean([bar(**{column: 123})])


def test_duplicate_column_labels_and_non_dataframe_fail():
    frame = pd.DataFrame([bar()])
    with pytest.raises(SchemaValidationError):
        cleaning.clean_historical_dataset(pd.concat([frame, frame[["close"]]], axis=1))
    with pytest.raises(SchemaValidationError):
        cleaning.clean_historical_dataset([bar()])


def test_no_imputation_no_synthetic_sessions_no_statistical_outlier_removal():
    result = clean([
        bar(date="2024-01-10", open=1e6, high=2e6, low=1e5, close=1e6),
        bar(date="2024-01-08", close=None), bar(date="2024-01-05"),
        bar(date="2024-01-09", open=1e-9, high=2e-9, low=1e-10, close=1e-9),
        bar(date="2024-01-05", symbol="MSFT"),
    ])
    assert result.dataframe.date.dt.strftime("%Y-%m-%d").tolist() == [
        "2024-01-05", "2024-01-09", "2024-01-10", "2024-01-05"]
    assert result.dataframe.close.tolist() == [11, 1e-9, 1e6, 11]


@pytest.mark.parametrize("rows", [[], [bar(close=None)], [bar(date="2024-02-01")]])
def test_empty_result_is_error(rows):
    with pytest.raises(DataCleaningError, match="No valid rows remain after cleaning"):
        cleaning.clean_historical_dataset(pd.DataFrame(rows, columns=COLUMNS), as_of_date=AS_OF)


@pytest.mark.parametrize("value", ["bad", "2024-01-05T12:00:00", "2024-01-05Z", 123, pd.NaT])
def test_invalid_as_of_date_is_dataset_error(value):
    with pytest.raises(DataCleaningError, match="as_of_date"):
        cleaning.clean_historical_dataset(pd.DataFrame([bar()]), as_of_date=value)


def test_as_of_is_inclusive_and_default_uses_local_today(monkeypatch):
    class Clock(date):
        @classmethod
        def today(cls):
            return cls(2024, 1, 5)

    monkeypatch.setattr(cleaning, "date", Clock)
    result = cleaning.clean_historical_dataset(pd.DataFrame([bar(), bar(date="2024-01-08")]))
    assert len(result.dataframe) == 1
    assert result.report.removal_reasons["future_date"] == 1


def test_ingestion_dataframe_is_already_clean(payload):
    from stocklab_ml.data.validation import build_dataframe, validate_request
    frame = build_dataframe(payload, validate_request("AAPL", "2024-01-01", "2024-01-09"))
    result = cleaning.clean_historical_dataset(frame, as_of_date=AS_OF)
    pd.testing.assert_frame_equal(frame, result.dataframe)
    assert result.report.rows_removed == 0
