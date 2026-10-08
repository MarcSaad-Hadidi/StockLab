# SQL Server CI implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A green CI must include every discovered SQL Server test, without skipped scenarios.

**Architecture:** Keep non-SQL backend tests on Ubuntu. Add a Windows 2022 job that starts LocalDB, enables the existing SQL fixtures and compares discovery with the final TRX report. Price-alert fixtures create disposable LocalDB schemas while retaining their existing explicit-connection smoke mode.

**Tech Stack:** GitHub Actions, .NET 10, SQL Server LocalDB, Python standard library.

**Spec:** https://github.com/MarcSaad-Hadidi/StockLab/issues/222

## Global constraints

- Branch `chore/sql-server-tests-ci`; PR to `develop` only after the user requests it.
- No ML, production database, application behavior or personal-document changes.
- SQL failures and skips must fail the job; no secrets are needed for LocalDB.

## Review focus

- Empty discovery and missing/malformed reports fail validation.
- Missing parameterized cases, skipped tests and inconsistent counters fail validation.
- Every SQL fixture owns only its generated test database; explicit external smoke connections retain row-only cleanup.
- Non-SQL and SQL filters partition the backend suite.
- Verify the workflow on GitHub Actions before reporting CI success.

### Task 1: Execute and enforce SQL coverage

**Files:** `.github/workflows/ci.yml`, `.github/scripts/verify_sql_test_results.py`, `.github/scripts/tests/test_verify_sql_test_results.py`, `backend/StockLab.UnitTests/Api/PriceAlertsSqlServerApiTests.cs`, `backend/StockLab.UnitTests/Api/PriceAlertsApiTests.cs`, `backend/README.md`.

**Interface:** Validator CLI accepts `--trx` and `--expected-tests`; zero exit requires exact discovery/result coverage and every result passed.

- [ ] Write validator regressions for good, missing, malformed, skipped, failed, empty, duplicated and mismatched results; observe failure before implementing the validator.
- [ ] Enable the four existing price-alert SQL cases on isolated LocalDB schemas, then run all SQL tests with a discovery manifest and validate the TRX.
- [ ] Add the Windows SQL job, result artifacts and non-SQL filter; document both local commands.
- [ ] Run the complete backend suite, validator tests and workflow validation; commit, push and dispatch CI on the branch without opening a PR.
