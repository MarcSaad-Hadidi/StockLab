# StockLab Machine Learning

## Purpose

StockLab's independent Python ML module will receive historical data, clean it,
prepare features, train models, and produce prediction probabilities followed by
BUY / SELL / HOLD signals with confidence. It will never execute a trade.
The frontend and .NET backend are not required to install or test this module.

## Requirements

Use **Python 3.12.x** across the team, with pip and the standard-library `venv`
module available. Use a current stable 3.12 patch release.
Python 3.12 is officially supported by all four selected dependencies:
[NumPy](https://pypi.org/project/numpy/2.5.3/),
[pandas](https://pypi.org/project/pandas/3.0.6/),
[scikit-learn](https://pypi.org/project/scikit-learn/1.9.1/), and
[pytest](https://pypi.org/project/pytest/9.1.1/).

## Setup — Windows

From the repository root, in PowerShell, with Python 3.12 on `PATH`:

```powershell
cd ml
python --version
python -m venv .venv
.\.venv\Scripts\Activate.ps1
```

Verify that the version is `3.12.x` before creating the environment. If the
Windows Python launcher selects another version, use `py -3.12 -m venv .venv`.
In Command Prompt, activate with `.venv\Scripts\activate.bat` instead.
If PowerShell blocks activation, use Command Prompt or invoke
`.\.venv\Scripts\python.exe` in place of `python` in the commands below.

## Setup — macOS/Linux

From the repository root, in bash/zsh:

```bash
cd ml
python3.12 --version
python3.12 -m venv .venv
source .venv/bin/activate
```

If `python` already selects Python 3.12, `python -m venv .venv` is equivalent.
Some Linux distributions require the Python 3.12 `venv` OS package first.

## Install dependencies

With the environment active, from `ml/`:

```shell
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
```

The four direct dependencies are NumPy, pandas, scikit-learn, and pytest.
The version ranges start at verified stable releases and allow patch updates
within their minor release lines. Transitive dependencies are resolved by pip;
this is a bounded development setup, not an exact environment lock.

## Run tests

From `ml/`, with the environment active:

```shell
python -m pytest
```

The smoke tests import `numpy`, `pandas`, `sklearn`, and `stocklab_ml` without
network calls. `pytest.ini` adds `src/` using pytest's built-in `pythonpath`
setting, relative to that configuration file. No per-file path changes or
packaging dependencies are needed. The package is currently importable in tests;
it is not installed as a distribution or exposed as a command-line application.
From the repository root, `python -m pytest ml` also works with this environment.

## Project structure

```text
ml/
├── README.md
├── requirements.txt
├── pytest.ini
├── src/
│   └── stocklab_ml/
│       └── __init__.py
├── tests/
│   └── test_environment.py
├── data/
│   ├── raw/
│   │   └── .gitkeep
│   └── processed/
│       └── .gitkeep
└── models/
    └── .gitkeep
```

The local `.venv/` is ignored by Git. Future ML code belongs in
`src/stocklab_ml/`; notebooks will not be the source of truth.

## Data policy

Raw datasets in `data/raw/`, processed datasets in `data/processed/`, and trained
models in `models/` stay local and unversioned. Git ignores all contents of these
directories, including nested files and model formats such as `.pkl`, `.joblib`,
`.pickle`, and `.onnx`; only the empty `.gitkeep` placeholders are tracked.
Do not store generated datasets or models outside these directories.

## Secrets

No API key is needed for #54. Issue #56 will use `TWELVE_DATA_ML_API_KEY` for
Python ML only. Never commit its value or any other secret.

The Twelve Data Website key (`TwelveData:Keys:Website`) belongs exclusively to
the .NET backend's Market / Stock Details features. Python ML must not use it,
and the Website backend must not use `TWELVE_DATA_ML_API_KEY`.
There is no fallback between the two keys.

## Architecture invariant

- **ML = WHAT**: future signals and confidence.
- **Risk Manager = WHETHER + HOW MUCH**: decide whether a trade is allowed and its size.
- **Paper Trading Engine = EXECUTE**: execute approved simulated trades.

ML never executes trades. These responsibilities are documented only at this stage.

## Current status

**#54 = environment only.** No data retrieval, cleaning, feature engineering,
training, predictions, risk management, backtesting, frontend/API integration,
AWS, or Azure is implemented. Installation downloads Python packages; tests
need no external service, market-data call, API key, or API credit.
Historical data ingestion belongs to #56 and has not started.
