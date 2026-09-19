"""All tests are offline; only the HTTP transport is replaced."""

import json
import socket

import pytest
import requests


@pytest.fixture(autouse=True)
def offline(monkeypatch):
    def forbidden(*args, **kwargs):
        raise AssertionError("Network is forbidden during pytest")

    monkeypatch.setattr(socket.socket, "connect", forbidden)
    monkeypatch.setattr(socket.socket, "connect_ex", forbidden)
    monkeypatch.setenv("TWELVE_DATA_ML_API_KEY", "ML-TEST-KEY")


@pytest.fixture
def payload():
    return {
        "meta": {
            "symbol": "AAPL", "interval": "1day", "currency": "USD",
            "exchange_timezone": "America/New_York", "exchange": "NASDAQ",
            "mic_code": "XNAS", "type": "Common Stock",
        },
        "values": [
            {"datetime": day, "open": "100.5", "high": "103.25",
             "low": "99.75", "close": "102.0", "volume": "123456"}
            for day in ["2024-01-08", "2024-01-05", "2024-01-04",
                        "2024-01-03", "2024-01-02"]
        ],
        "status": "ok",
    }


@pytest.fixture
def transport(monkeypatch, payload):
    class Transport:
        calls = None
        body = None
        status = 200
        error = None

        def send(self, session, request, **kwargs):
            self.calls.append((request, kwargs))
            if self.error:
                raise self.error
            response = requests.Response()
            response.status_code = self.status
            response._content = (json.dumps(self.body).encode()
                                 if not isinstance(self.body, bytes) else self.body)
            response._content_consumed = True
            response.request = request
            response.url = request.url
            return response

    stub = Transport()
    stub.calls = []
    stub.body = payload
    monkeypatch.setattr(requests.Session, "send",
                        lambda session, request, **kw: stub.send(session, request, **kw))
    return stub
