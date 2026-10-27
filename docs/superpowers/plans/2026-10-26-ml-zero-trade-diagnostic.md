# ML zero-trade diagnostic PR (#273, first stage)

**Goal:** Make the verified zero-trade explanation reproducible and reviewable without publishing private inputs or modifying the trading policy.

**Scope:** A read-only offline diagnostic for the existing two-baseline catalog exporter, synthetic tests, a sanitized aggregate report and ML documentation. No model promotion, new candidate, threshold change, backend, UI, database or cloud work. This PR advances #273 and does not close its remaining phases.

**Approach:** Reuse cleaning, features, baseline training, registry binding, signal/confidence generation and the existing backtest engine. Recreate estimators only in memory, verify original version/holdout metadata and the full backtest report, then report aggregate probabilities, signal counts and engine skip counts. Require one version per baseline catalog; fail on ambiguity or mismatches instead of claiming a reproduced result. CLI output/errors omit source paths, raw rows and exception details.

- [x] Write failing synthetic-catalog tests for exact reproduction, immutability, ambiguous/stale metadata/report refusal and safe actual CLI behavior.
- [x] Add `ml/scripts/diagnose_zero_trades.py` without changing existing model or execution policy; run targeted checks and the real private catalog with before/after fingerprints.
- [x] Add the authorized aggregate diagnosis and usage in `ml/README.md`; independent read-only review, resolve findings, then full ML tests.
- [ ] Publish the authorized summary to #273, create/attach a PR to develop, and verify all CI checks. Do not publish private catalog data, local paths or per-row audits.

## Verification ledger

- RED: importing the diagnostic failed before its implementation; the first 8 tests then passed. Extended to 10 cases with a nonzero-trade synthetic control, missing versions and boolean/numeric report tampering; all passed.
- Real private catalog: both reports reproduced with the tracked CLI; before/after SHA-256 fingerprints of every catalog file matched. Output stayed aggregate-only, with no source paths or private rows. The detailed audit remains ignored locally.
- Review found argparse could echo a private value for unknown options/positionals. Both regression cases failed as expected, then argument errors were changed to a fixed message without echoing the supplied arguments.
- Final targeted suite: 12 passed. Independent read-only review confirmed the finding resolved with no remaining blocker.
- Final full ML suite: 1,621 passed, including all 12 diagnostic cases. No implementation changes after this run.
- The authorized aggregate summary is published on #273; the issue remains open for later ML phases.
