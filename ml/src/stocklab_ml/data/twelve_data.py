"""One synchronous Twelve Data request using the ML credential exclusively."""

import json
import os
import re

import requests

from .models import (
    ConfigurationError, HistoricalRequest, InvalidProviderResponseError,
    ProviderAuthenticationError, ProviderRateLimitError, ProviderUnavailableError,
)

ENDPOINT = "https://api.twelvedata.com/time_series"


def _provider_error(code: object) -> None:
    # Never relay provider text, response URLs, or underlying transport errors.
    if code in (401, 403, "401", "403"):
        raise ProviderAuthenticationError("Twelve Data rejected the ML credential or its permissions.")
    if code in (429, "429"):
        raise ProviderRateLimitError("Twelve Data credit or rate limit reached; no retry performed.")
    if isinstance(code, int) and code >= 500:
        raise ProviderUnavailableError("Twelve Data is unavailable; no retry performed.")
    raise InvalidProviderResponseError("Twelve Data rejected the request or returned an unexpected HTTP status.")


def fetch_historical(request: HistoricalRequest) -> dict:
    key = os.environ.get("TWELVE_DATA_ML_API_KEY", "").strip()
    if not key:
        raise ConfigurationError("TWELVE_DATA_ML_API_KEY is not configured.")
    # Header authentication keeps credentials out of URLs and urllib3 URL logs.
    try:
        with requests.Session() as session:
            session.trust_env = False  # no .netrc authentication or ambient proxy credentials
            session.mount("https://", requests.adapters.HTTPAdapter(max_retries=0))
            with session.get(
                ENDPOINT, params=request.params,
                headers={"Authorization": f"apikey {key}"},
                timeout=20, allow_redirects=False,
            ) as response:
                if response.status_code != 200:
                    _provider_error(response.status_code)
                try:
                    payload = response.json()
                except ValueError:
                    raise InvalidProviderResponseError("Twelve Data returned invalid JSON.") from None
    except requests.RequestException:
        raise ProviderUnavailableError("Twelve Data request failed or timed out; no retry performed.") from None
    if not isinstance(payload, dict):
        raise InvalidProviderResponseError("Twelve Data returned an invalid response object.")
    if payload.get("status") == "error" or "code" in payload:
        _provider_error(payload.get("code"))
    # Reject credential-bearing payloads before any persistence. Benign raw JSON
    # stays unchanged, including metadata and the original ordering of values.
    try:
        serialized = json.dumps(payload, ensure_ascii=False, allow_nan=False)
    except ValueError:
        raise InvalidProviderResponseError("Provider response contains non-finite JSON numbers.") from None
    if key in serialized or re.search(
        r'(?i)(?:"(?:apikey|api_key|authorization|token|secret)"\s*:|apikey\s*=|authorization\s*:)',
        serialized,
    ):
        raise InvalidProviderResponseError("Provider response contains credential material; nothing saved.")
    return payload
