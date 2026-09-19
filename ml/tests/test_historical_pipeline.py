"""Contract and failure tests for ingestion, without external API calls."""

from copy import deepcopy
from datetime import date, timedelta
import json
import traceback
from urllib.parse import parse_qs, urlsplit

import pandas as pd
import pytest
import requests


def run(tmp_path, **kwargs):
    from stocklab_ml.data.pipeline import prepare_historical_dataset
    return prepare_historical_dataset(
        kwargs.pop("symbol", " aapl "), kwargs.pop("start_date", "2024-01-01"),
        kwargs.pop("end_date", "2024-01-09"), data_dir=tmp_path, **kwargs,
    )


def test_pipeline_contract(tmp_path, transport, payload):
    result = run(tmp_path)
    frame = result.dataframe
    assert result.symbol == "AAPL" and result.rows == 5
    assert result.start_date == date(2024, 1, 2)
    assert result.end_date == date(2024, 1, 8)
    assert result.metadata.exchange == "NASDAQ"
    assert list(frame.columns) == ["date", "symbol", "open", "high", "low", "close", "volume"]
    assert str(frame.date.dtype) == "datetime64[ns]"
    assert str(frame.symbol.dtype) == "string"
    assert all(str(frame[col].dtype) == "float64" for col in ["open", "high", "low", "close"])
    assert str(frame.volume.dtype) == "Int64"
    assert frame.date.dt.strftime("%Y-%m-%d").tolist() == [
        "2024-01-02", "2024-01-03", "2024-01-04", "2024-01-05", "2024-01-08"]
    assert frame.open.tolist() == [100.5] * 5
    assert frame.volume.tolist() == [123456] * 5
    assert frame.index.tolist() == list(range(5))
    assert not frame.duplicated(["symbol", "date"]).any()
    assert result.raw_path.name == "AAPL_1day_2024-01-01_2024-01-09.json"
    assert json.loads(result.raw_path.read_text()) == payload
    csv = result.processed_path.read_text()
    assert csv.splitlines()[0] == "date,symbol,open,high,low,close,volume"
    assert "ML-TEST-KEY" not in csv + result.raw_path.read_text()
    assert len(transport.calls) == 1
    request, options = transport.calls[0]
    url = urlsplit(request.url)
    assert f"{url.scheme}://{url.netloc}{url.path}" == "https://api.twelvedata.com/time_series"
    assert parse_qs(url.query) == {
        "symbol": ["AAPL"], "interval": ["1day"], "start_date": ["2024-01-01"],
        "end_date": ["2024-01-09"], "order": ["asc"], "format": ["JSON"], "adjust": ["splits"]}
    assert request.headers["Authorization"] == "apikey ML-TEST-KEY"
    assert "ML-TEST-KEY" not in request.url
    assert options["allow_redirects"] is False
    assert 10 <= options["timeout"] <= 30


@pytest.mark.parametrize("kwargs", [
    {"symbol": ""}, {"symbol": "AAPL,MSFT"}, {"symbol": "../AAPL"},
    {"symbol": "AAPL?apikey=x"}, {"symbol": None},
    {"start_date": "2024-02-30"}, {"start_date": "20240101"},
    {"start_date": "2024-01-01T00:00:00"}, {"start_date": None},
    {"start_date": "2024-01-09"}, {"start_date": "2024-01-10"},
    {"end_date": date.today().isoformat()},
    {"end_date": (date.today() + timedelta(days=1)).isoformat()},
    {"start_date": "1990-01-01"},
])
def test_bad_arguments_do_not_spend_credits(tmp_path, transport, kwargs):
    from stocklab_ml.data.models import HistoricalDataError
    with pytest.raises(HistoricalDataError):
        run(tmp_path, **kwargs)
    assert not transport.calls
    assert not list(tmp_path.rglob("*.csv"))


@pytest.mark.parametrize("symbol,provider_symbol,exchange,encoded", [
    (" brk.b ", "BRK.B", "NYSE", "BRK.B"),
    ("tsla:nasdaq", "TSLA", "NASDAQ", "TSLA%3ANASDAQ"),
    ("0700:hkex", "0700", "HKEX", "0700%3AHKEX"),
    ("BTC/USD", "BTC/USD", "Coinbase", "BTC%2FUSD"),
])
def test_symbols_remain_identifiable_and_filenames_safe(
    tmp_path, transport, symbol, provider_symbol, exchange, encoded,
):
    transport.body["meta"].update(symbol=provider_symbol, exchange=exchange)
    result = run(tmp_path, symbol=symbol)
    assert result.symbol == symbol.strip().upper()
    assert result.raw_path.parent == tmp_path / "raw"
    assert result.raw_path.name.startswith(encoded + "_1day_")
    query = parse_qs(urlsplit(transport.calls[0][0].url).query)
    assert query["symbol"] == [provider_symbol]
    if ":" in symbol:
        assert query["exchange"] == [exchange]


@pytest.mark.parametrize("field,value", [
    ("open", "garbage"), ("close", "NaN"), ("high", "inf"),
    ("low", "-inf"), ("open", None), ("close", True), ("open", 0),
    ("close", -1), ("high", "100"), ("low", "101"),
    ("volume", "-1"), ("volume", "1.2"), ("volume", "1e3"),
    ("volume", True), ("volume", None), ("volume", str(2**63)),
    ("datetime", "2024-01-02T00:00:00"), ("datetime", "2024-02-30"),
    ("datetime", "2023-12-31"), ("datetime", "2024-01-10"),
])
def test_invalid_bar_is_rejected_without_processed_output(tmp_path, transport, field, value):
    from stocklab_ml.data.models import DatasetValidationError
    transport.body["values"][0][field] = value
    with pytest.raises(DatasetValidationError):
        run(tmp_path)
    assert len(transport.calls) == 1
    assert len(list(tmp_path.rglob("*.json"))) == 1  # raw retained for diagnosis
    assert not list(tmp_path.rglob("*.csv"))


@pytest.mark.parametrize("field", ["datetime", "open", "high", "low", "close", "volume"])
def test_missing_required_bar_field(tmp_path, transport, field):
    from stocklab_ml.data.models import DatasetValidationError
    del transport.body["values"][0][field]
    with pytest.raises(DatasetValidationError):
        run(tmp_path)


def test_duplicates_fail_without_deduplication(tmp_path, transport):
    from stocklab_ml.data.models import DatasetValidationError
    transport.body["values"].append(deepcopy(transport.body["values"][0]))
    with pytest.raises(DatasetValidationError, match="Duplicate"):
        run(tmp_path)


@pytest.mark.parametrize("body", [None, [], {}, {"status": "ok", "meta": {}, "values": []},
                                     b"not JSON"])
def test_malformed_or_empty_response(tmp_path, transport, body):
    from stocklab_ml.data.models import HistoricalDataError
    transport.body = body
    with pytest.raises(HistoricalDataError):
        run(tmp_path)
    assert not list(tmp_path.rglob("*.csv"))


@pytest.mark.parametrize("field,value", [("symbol", "MSFT"), ("interval", "1min"),
                                          ("currency", None), ("exchange", 123)])
def test_metadata_mismatch(tmp_path, transport, field, value):
    from stocklab_ml.data.models import InvalidProviderResponseError
    transport.body["meta"][field] = value
    with pytest.raises(InvalidProviderResponseError):
        run(tmp_path)


@pytest.mark.parametrize("size", [4900, 4999, 5000, 5001])
def test_possible_truncation_fails_safely(tmp_path, transport, size):
    from stocklab_ml.data.models import RangeTooLargeError
    transport.body["values"] = [transport.body["values"][0]] * size
    with pytest.raises(RangeTooLargeError, match="single-request limit"):
        run(tmp_path)
    assert len(transport.calls) == 1


@pytest.mark.parametrize("status,category", [(401, "ProviderAuthenticationError"),
    (403, "ProviderAuthenticationError"), (429, "ProviderRateLimitError"),
    (500, "ProviderUnavailableError"), (400, "InvalidProviderResponseError"),
    (404, "InvalidProviderResponseError"), (302, "InvalidProviderResponseError")])
@pytest.mark.parametrize("http_error", [False, True])
def test_safe_provider_errors(tmp_path, transport, status, category, http_error, caplog):
    from stocklab_ml.data import models
    transport.status = status if http_error else 200
    transport.body = {"status": "error", "code": status,
                      "message": "apikey=ML-TEST-KEY https://unsafe.example"}
    with pytest.raises(getattr(models, category)) as exc:
        run(tmp_path)
    assert "ML-TEST-KEY" not in "".join(traceback.format_exception(exc.value)) + caplog.text
    assert len(transport.calls) == 1
    assert not list(tmp_path.rglob("*.json"))


@pytest.mark.parametrize("error", [requests.Timeout("ML-TEST-KEY"),
                                  requests.ConnectionError("ML-TEST-KEY")])
def test_transport_failure_is_safe_and_never_retried(tmp_path, transport, error):
    from stocklab_ml.data.models import ProviderUnavailableError
    transport.error = error
    with pytest.raises(ProviderUnavailableError) as exc:
        run(tmp_path)
    assert "ML-TEST-KEY" not in "".join(traceback.format_exception(exc.value))
    assert len(transport.calls) == 1


def test_missing_ml_key_has_no_website_fallback(tmp_path, transport, monkeypatch):
    from stocklab_ml.data.models import ConfigurationError
    monkeypatch.delenv("TWELVE_DATA_ML_API_KEY")
    monkeypatch.setenv("TwelveData__Keys__Website", "WEBSITE-TEST-KEY")
    with pytest.raises(ConfigurationError, match="TWELVE_DATA_ML_API_KEY is not configured"):
        run(tmp_path)
    assert not transport.calls


def test_echoed_key_never_reaches_disk(tmp_path, transport):
    from stocklab_ml.data.models import InvalidProviderResponseError
    transport.body["meta"]["name"] = "ML-TEST-KEY"
    with pytest.raises(InvalidProviderResponseError) as exc:
        run(tmp_path)
    assert "ML-TEST-KEY" not in str(exc.value)
    assert not list(tmp_path.rglob("*.json"))


def test_overwrite_requires_opt_in_and_does_not_spend_credits(tmp_path, transport):
    from stocklab_ml.data.models import StorageError
    first = run(tmp_path)
    raw, csv = first.raw_path.read_bytes(), first.processed_path.read_bytes()
    with pytest.raises(StorageError):
        run(tmp_path)
    assert len(transport.calls) == 1
    second = run(tmp_path, overwrite=True)
    assert len(transport.calls) == 2
    assert second.raw_path.read_bytes() == raw
    assert second.processed_path.read_bytes() == csv
    pd.testing.assert_frame_equal(first.dataframe, second.dataframe)


def test_zero_volume_is_preserved(tmp_path, transport):
    transport.body["values"][0]["volume"] = "0"
    assert run(tmp_path).dataframe.volume.iloc[-1] == 0


@pytest.mark.parametrize("value", [float("nan"), float("inf"), -float("inf")])
def test_nonstandard_json_numbers_are_controlled_errors(tmp_path, transport, value):
    from stocklab_ml.data.models import HistoricalDataError
    transport.body["values"][0]["open"] = value
    with pytest.raises(HistoricalDataError):
        run(tmp_path)
    assert not list(tmp_path.rglob("*.csv"))


@pytest.mark.parametrize("key", ["apikey", "api_key", "authorization", "token", "secret"])
def test_credential_fields_are_never_saved(tmp_path, transport, key):
    from stocklab_ml.data.models import InvalidProviderResponseError
    transport.body["meta"][key] = "some-other-credential"
    with pytest.raises(InvalidProviderResponseError):
        run(tmp_path)
    assert not list(tmp_path.rglob("*.json"))


def test_standalone_existing_csv_blocks_network(tmp_path, transport):
    from stocklab_ml.data.models import StorageError
    path = tmp_path / "processed" / "AAPL_1day_2024-01-01_2024-01-09.csv"
    path.parent.mkdir()
    path.write_text("existing dataset")
    with pytest.raises(StorageError):
        run(tmp_path)
    assert not transport.calls
    assert path.read_text() == "existing dataset"


def test_raw_reprocessing_is_independent_of_provider_order(tmp_path, transport):
    from stocklab_ml.data.validation import build_dataframe, validate_request
    result = run(tmp_path)
    raw = json.loads(result.raw_path.read_text())
    raw["values"].reverse()
    replay = build_dataframe(raw, validate_request("AAPL", "2024-01-01", "2024-01-09"))
    pd.testing.assert_frame_equal(result.dataframe, replay)
