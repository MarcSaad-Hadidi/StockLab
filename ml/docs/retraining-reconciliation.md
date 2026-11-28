# Local RF retraining after observed volume revisions

The existing local retraining scope rejected two revised provider-volume cells with `DATA_REVISION_CONFLICT`. A separate corrected bootstrap and journal now complete a real local retraining run. Exactly one RF candidate `v000008` is appended as **Archived**; `v000007` remains **Active**. This repairs the retraining lineage and does not qualify or activate a new strategy. The boosted >20%/sales/drawdown goal remains unfinished.

## Preserved and rebuilt data

The [protocol](../../docs/superpowers/plans/2026-11-13-retraining-reconciliation.md) was committed at `30e46b6` before creating the bootstrap or fitting. Both source vintages are retained and hash-bound. Among7,345 common rows, only November6 volumes differ: AAPL44,676,300→44,703,800 and MSFT26,938,400→26,956,400. All OHLC and other volumes match. The original raw responses contain those exact values; the provider's reason for revising them is unknown.

Following the [existing reconciliation contract](../README.md#new-observations-incremental-refresh-and-credit-budget), current November12 source data is clipped inclusively to the old November6 watermark into a NEW bootstrap/config/journal scope. The original bootstrap, config, journal and immutable snapshots remain byte-identical. Actual preparation occurs November13; the rebuild is not evidence that corrected values were available November6. Only dataset_path/state_path differ in the new config; registry/artifact roots, symbols, recent-history-v1, parameters and cadence are preserved.

The bootstrap has7,345 rows and SHA`f62ed7610dec7947f927e77e93e4641b45c2beead18397bc1795fc43b129a380`. The real scheduler's conservative cutoff is November11 at the current UTC clock. The new-scope dry-run returns **DRY_RUN_READY** with three new sessions per symbol/fifteen new rows, zero fitting/provider/network/publication/lock attempts and no durable journal/data/registry change.

## One actual operational training run

Run`a516d39b-3b0c-45cd-87e3-b3ceef8495c3` completes November13 at06:39:29.687729UTC. `now_utc=None` retains real training/publication clocks. Its new frozen dataset has7,360 rows through November11; observed calls are exactly **one RF fit, one Logistic comparison fit and one scaler fit**. Provider/network, HGBT/calibration and promotion counters are zero. No portfolio producer is imported or called; that last boundary is structural, not an instrumented replay counter.

The recent profile selects5,265 feature rows. There are5,005 actual training rows, five purged boundary rows,250 holdout rows across50 dates and five unknown next-close tails. Training features run2022-09-01 through2026-08-28; every training target precedes the2026-09-01 holdout boundary. Holdout features endNovember10 with targets observed throughNovember11. Seven legacy inputs and target_up_1d remain explicit; these are not boosted28/20-session forecasts.

Actual fit-input copies, exact keys/targets, scaler statistics, raw/scaled matrices and both real RF/Logistic holdout predictions are exported before/around the unchanged calls. The original x/y objects are passed unchanged to fitting. Evidence-only conversion of nullable-volume arrays tofloat64 is checked separately without a model fit.

|Recent holdout evidence|RF candidate|
|---|---:|
|Accuracy|50.0%|
|Precision|51.28%|
|Recall|62.02%|
|F1|.56140|
|Rows / dates|250 /50|
|Logistic comparison accuracy|52.8%|

These correlated five-stock observations do not establish strong forecast quality. The genuine new fit reproduces ActiveRFv7's model binary, training report, metrics and holdout hash exactly. This is deterministic reproduction at a new actual training time, not a measured prediction improvement. RFv8 therefore stays Archived.

The actual RF binary SHA is`b6f68e7be874f77c0d2a379c4e6c62603d1ae7ee5399dc4303a932e297761c20`; manifest SHA`09a8df8a4efd0ce94b5711626b866dd40812d4b4ccf5e0714862b87aae07f9e3`. The shared registry legitimately changes from4a3ffa5d… to e6cdede9… by one Archived append. All eleven pre-existing records and existing artifact bytes/absences remain unchanged. Previous proof-period registry assertions and their archives are preserved without rewriting or rebinding them. The current registry is not claimed byte-identical after this later operational publication.

## Verification and scheduler handoff

The existing reconciliation regression passes unchanged with real NTFS atomic hardlinks. Its initial restricted-sandbox setup could not publish the fixture with os.link; the same check passes outside that restriction on a new owned test directory. Its two miniature Logistic fits are separate from the operational RF/Logistic budget. All53 old research sources and84 snapshot bindings remain unchanged.

The independent file-only audit verifies 105,105 exact fit-matrix values across RF raw inputs, scaler inputs and scaled Logistic inputs, alongside the 250 holdout rows, metrics and registry/artifact/journal bindings. Its report SHA is `2b2a8005a13ca704c74fbd149d03b3a329718d5c679fbb6b92a4e64e5d4df982`. Two narrow corrections to the checker handled the explicit nine-key runtime counters and the original `(symbol, date)` feature order. Both initial errors are preserved. The auditor performs zero fits, estimator loads or producer calls.

After that audit, the private scheduler wrapper changed its config path to the reconciled scope. The cadence smoke returned `SKIPPED_NOT_DUE` with zero fits or provider requests. The task remained disabled during the user's pause.

On November 25 in Toronto, the existing paper task restarted and completed the November 25 session with five HOLD decisions. The validated snapshot gave the reconciled retraining scope nine new sessions per symbol through November 24 under its conservative cutoff. The dry-run returned `DRY_RUN_READY` with zero API calls. Windows's disable operation had inserted an `Enabled=false` XML element where the original definition omitted the default. Comparing the rest of the task definition showed all other fields equal, so the original enabled state and schedule were restored. The real Windows task ran once, returned exit code 0 and published **Archived RFv9**, with RFv7 Active before and after. The run used the validated local snapshot and reported zero API requests. RFv9's recent holdout accuracy is 53.2% and F1 is .55172. These are RF next-close metrics and do not evaluate the booster's 20-session strategy. The local paper task remained Running, and the retraining task returned to Ready with its scheduled trigger enabled.

Private evidence: `models/retraining-reconciliation-proof-20261113` and `models/retraining-reconciled-20261113`. Prep result SHA`683b277300d3ea07acadaef3ce0bd33f438aa4da07e0ab64ba3ea55e0ec67936`; dry-run SHA`cb8f2a72688db6bcae814604fcc9c23204ecce2d512f8079b24b6bdef24523a4`; actual-run SHA`3bd8707e724a3b5adfcb1520bfcea967176a6b67c0f2b862e7ae0535bc5871c5`. The fixed protocol remains unchanged.

Future provider corrections still fail closed and need explicit reconciliation. The successful manual Windows task invocation validates the configured launcher and paths. Operation after reboot and the next calendar-triggered invocation remain to be observed on the PC.
