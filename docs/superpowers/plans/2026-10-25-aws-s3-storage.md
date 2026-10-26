# AWS S3 ML integration implementation plan

> Execute inline with test-driven development and verification before completion.

**Goal:** Complete issue #48 on `feature/aws-s3-storage`, targeting `develop`.

**Spec:** The user's attached issue #48 prompt; existing model artifact contracts remain authoritative.

**Architecture:** Reuse the model store's secure S3 transport for immutable file bundles. Dataset and backtest adapters derive exact keys; SHA-256 manifests commit complete bundles. A local workflow composes cleaning, causal features, chronological model training, existing signals and the existing backtest engine. Only an explicit flag enables AWS.

**Stack:** Python 3.12, existing pandas/scikit-learn/boto3/pytest dependencies.

## Constraints and review focus

- Existing bucket `stocklab-ml-248557779705`, region `ca-central-1`, prefix `ml`, profile `stocklab-dev`; create no AWS resources or credentials.
- Only GetObject/PutObject; no delete, LIST, public ACL, order execution or provider API calls.
- Preserve file bytes and JSON metadata; strict bounded manifests; no untrusted paths from S3.
- Race-safe conditional writes, partial upload recovery, corrupted/missing files, 403 without ListBucket, interrupted downloads and local collisions need tests.
- Training uses only the chronological train partition; backtests use holdout predictions, never training predictions.
- Synthetic evidence is explicitly labelled and makes no claim about real investment performance.

## Task 1: Dataset and backtest file storage

Files: shared transport in `ml/src/stocklab_ml/artifacts/s3_storage.py`; bundle operations in `artifacts/s3_files.py`; adapters in `data/s3_storage.py` and `backtesting/s3_storage.py`; `ml/tests/test_s3_file_storage.py`.

- [x] Write failing round-trip, metadata, corruption, missing object, denied/network, concurrency, isolation, local collision and conditional SDK tests.
- [x] Run the new tests and observe missing functionality.
- [x] Extract existing transport without changing model behavior; implement bounded immutable manifests and no-clobber local publication with a final manifest marker.
- [x] Implement `S3DatasetStore.upload_dataset/download_dataset` and `S3BacktestStore.upload_backtest/download_backtest`.
- [x] Verify new tests and existing model artifact tests in the full suite.

## Task 2: Reproducible workflow and CLI

Files: `ml/src/stocklab_ml/backtesting/s3_workflow.py`, `ml/examples/s3_storage_smoke.py`, `ml/tests/test_s3_workflow.py`.

- [x] Write failing workflow tests proving deterministic outputs, chronological holdout, model recovery without fit, CSV input, offline default and explicit live gating.
- [x] Implement local run, optional S3 publication of raw/cleaned datasets, model artifact and four backtest files; exact downloads do not execute the workflow.
- [x] Provide CLI run/upload/download commands; optional real AWS round-trip requires an explicit live flag and the approved bucket/region/prefix/profile.
- [x] Run offline smoke and tests with sockets blocked.

## Task 3: Documentation, full verification and PR

Files: `ml/README.md` and this completion ledger.

- [x] Document configuration, object organization, commands, permissions, provenance, recovery and limits.
- [x] Run the complete ML suite and report any baseline failures accurately.
- [x] Review the diff, run `git diff --check`, and prepare the commit and PR body targeting `develop` with `Closes #48`.

## Evidence / decisions

- Initial inspection: issue #48 open; only #76 model S3 storage exists; local checkout equals fetched `origin/develop` (`cb71e94`). No historical CSVs available in `ml/data`.
- User supplied a complete implementation specification and requested execution of missing work; proceed without another design approval round.
- Baseline restricted-sandbox suite: 1125 passed, 259 failed, 159 errors; direct `os.link` reproduction raised WinError 5. Existing atomic local writers require normal NTFS permissions. Tests are rerun outside the restricted Windows sandbox; do not weaken the writers.
- New storage/workflow tests: initial RED observed missing modules; first GREEN 25 storage tests and 6 workflow tests. Independent review found JSON bool/number equality and ambient AWS credential precedence issues. Regression tests reproduced four failures before fixes; canonical JSON comparison and a single explicitly named boto3 session fixed both. Final targeted run: 37 passed (26.28s).
- Offline smoke `ml/examples/s3_storage_smoke.py --output ml/results/s3-48-verified`: passed, 240 synthetic bars, 15 simulated trades, raw/cleaned datasets and all four backtest files recovered byte-identically; fitted model recovered and loaded with fit disabled; zero market API or live AWS calls.
- Local download directories are reserved with mkdir and completed with a final verified manifest rather than using directory replacement, preserving no-clobber semantics across Windows/POSIX. Local I/O failure may leave an incomplete directory; this limitation is documented.
- No real historical data, live AWS validation, infrastructure change, policy change or real trade was used.
- First normal-permission whole-suite run: 1573 passed, 1 failed in 919.93s. The failure was `test_journal_cadence_and_bootstrap_reference_cannot_be_reset[last_check_at_utc-None]`, an existing retraining journal publication/flush error. The retraining module and local writer were unchanged. Both parametrizations passed on an isolated recheck (16.21s); no unrelated code change was made without a reproducible cause.
- Final verification is running across three exhaustive, disjoint groups of existing test files (core, promotion, storage/retraining), with distinct temporary/cache directories, after the review fixes. Final result pending.
- That grouped run passed 1216 core and 127 promotion tests; storage/retraining had 236 passed and one journal publication failure in another test. Isolated recheck passed. A direct writer stress reproduction exposed the suppressed cause on write 4: `PermissionError`, WinError 5, in `os.replace`.
- Narrow necessary correction in the reused `data/storage.py`: retry only Windows replacement errors 5/32/33, at most four attempts with 10/20/40 ms delays, retaining the same atomic replacement and no-clobber behavior. Five regression cases reproduced four failures before the change. Post-fix targeted run: 42 passed in 25.80s. Persistent denials preserve original bytes; non-Windows denials do not retry. No retraining/promotion logic changed.
- The same independent writer reproduction then completed 10,000 writes with zero errors. Final offline smoke also passed under `ml/results/s3-48-final` (240 synthetic bars, 15 simulated trades, exact dataset/backtest recovery, model loaded with fit disabled).
- Final commands select all `ml/tests/test_*.py` files in disjoint groups: core excludes `^test_(promotion|model_promotion|retraining|s3_)`, promotion selects `^test_(promotion|model_promotion)`, storage/retraining selects `^test_(retraining|s3_)`. Each calls `python -m pytest <selected files> -q --basetemp=<fresh group dir> -o cache_dir=<fresh group cache> --tb=short`; logs are ignored under `ml/results/pytest-48-verified-*.log`. Final results pending.
- Final verified results after the writer fix: **1221 core passed (307.85s), 127 promotion passed (283.84s), 237 storage/retraining passed (377.28s): 1585 passed total across all 36 test files, zero failures.** Targeted 42 tests, 10,000 local writes, offline smoke, compilation, CLI help and diff checks also passed. Fetched `origin/develop` remained at the starting base before delivery. No live AWS test was executed.
