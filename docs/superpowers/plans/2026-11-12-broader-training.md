# Broader Training Data Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for training-panel validation and fitting/runner integration, with independent review. Data sampling is frozen before new history requests; strategy selection is frozen before any new returns.

**Goal:** Improve genuine out-of-sample ML entry/exit quality toward >=20%CAGR, SPY outperformance and lower DD, rather than tune thresholds/cadences on the same five stocks.

**Architecture:** Add a fixed broader TRAINING-ONLY observed-history panel to the existing five-stock model. Evaluation/execution remains the same five genuine cash-dividend stocks. Keep strict evaluation calendars and create separate contiguous per-identity training episodes; never invent pre-IPO/post-delist prices or bridge halts.

**Tech Stack:** Existing Python,pandas,scikit-learn,pytest and Twelve Data safe header-only transport. No dependencies, paid resources, accounts or live-model/task/DB changes.

**Spec:** Active user ML objective and rejected annual/monthly studies in `ml/docs/long-horizon-ml.md`.

## Frozen sampling / bounded acquisition

- Catalog inputs are the exact active/inclusive snapshots in `models/downside-20261112/catalog-probe`, with their manifest hashes. Metadata-eligible rows: USD/Common Stock, exchange NYSE or NASDAQ, advertised Basic, valid existing `SYMBOL_PATTERN` for ticker:exchange, four-character MIC. Exclude all five evaluation tickers regardless of venue. No screening on returns,current capitalization/liquidity, first history date, survival length or successful retrieval.
- Freeze first256 inclusive identities by ascending SHA256(`stocklab-broad-training-20261112-v1|MIC|symbol`), tie-breakMIC/symbol. Keep status solely in diagnostics, never model features or weights. No replacement of failures/short/ambiguous histories. Save full sampled catalog records, source hashes and this protocol before provider calls.
- PhaseA probes six members: first3 current and first3 inclusive-only in that same rank order, if present in the256. These are an access/identity/coverage probe, not selected strategy instruments. Exactly one split-adjusted daily request per identity,2013-04-01–2026-11-11,outputsize5000. This4973-calendar-day span respects the existing <5000-calendar-day request guard. It is not an IPO/membership interval.
- PhaseB, if ordinary historical access is feasible, completes only remaining members of the same256 (maximum256terminal identity outcomes plus16bounded transient retry attempts across both phases). Use ONLY user-authorized fallback credential, process-local/header authentication via existing sanitized transport. Pace below8credits/minute; global authentication/rate/quota failures stop acquisition with pending identities rather than marking them unavailable or replacing them. Resume only pending members after a verified cause/reset; retain all actual failures and raw successful responses with hashes. No other key or paid endpoint.
- Bind response symbol/exchange/MIC/USD/America-New_York/type to each requested catalog record; quarantine mismatch/ambiguity. Retain requested and observed dates, raw/clean rows/removals, internal missing SPY sessions, source basis and errors. No interpolation, rescaling or invented payout. All failures/short histories remain in the attrition manifest.
- Historical identities/terminal proceeds are not certified by catalog membership or reference access metadata. The panel is a RETROSPECTIVE PROVIDER-AVAILABLE SAMPLE, not point-in-time/survivor-free data. Include delisted histories when available; terminal unknown outcomes remain censored. No new-stock transaction returns are claimed without their corporate actions/cash events.

### Probe access diagnostic, before further requests

The six ticker probes complete: PLG3427rows,MBGL94,ATYR2897;SKX/RWOD/GTYH produce sanitized InvalidProviderResponseError. This does not establish that every delisted history is inaccessible or that a paid plan is needed. Before continuing, permit one additional SAME-IDENTITY diagnostic for SKX using public CUSIP830566105, verified in SEC2024-year-end ClassA disclosure `https://www.sec.gov/Archives/edgar/data/1065837/000201238325001330/xslSCHEDULE_13G_X01/primary_doc.xml`. Twelve documents CUSIP lookup on all plans. Same daily window/basis and identity checks; do not switch to a different company, new key or paid resource. Retain its result separately, never overwrite the ticker failure. One usage-counter read checks actual remaining free quota before PhaseB. These access diagnostics do not select by returns or change the256 sample.

## Training-panel rules

- Keep original strict `_matching_calendar` and account simulator unchanged for the five evaluation stocks. For a training identity, split actual observations into contiguous episodes of the verified SPY session calendar. Noncalendar/ambiguous identity rows fail closed. Never use a20-observation horizon that crosses an unresolved missing session. Each new episode resets feature warmup; record all discarded ranges.
- Derive monthly OHLCV only when every SPY session of that completed month occurs in the same episode: firstopen,maxhigh,minlow,lastclose,Python-integer sumvolume. Exclude incomplete boundary/current months. Preserve the same accepted history/RSI seed per identity and existing prior-confirmed-month-only availability; require full19-month context and252-session daily warmup. Explicitly label provenance as daily-derived, not native.
- Before new models, verify this aggregation against cached native monthly candles on the existing five. Independent preliminary audit finds exact OHLCV parity for1005complete stock/months(Feb2010–Oct2026) and4700finite monthly feature values when both contexts share the same start. Preserve initialization; do not compare different RSI seeds as exact parity.
- Reuse existing swing/downside feature and label functions on each episode with matching SPY slice. Upside t+21/downside t+20 full-known-date purges remain independent. Tail21/20 labels stay NaN; missing terminal prices are not negative examples or zero recovery. Keep episode/status/source exclusion counts.
- Train on original five PLUS usable sampled episodes, predict/evaluate only the original five. Use the same four-year ANNUAL refits, RF300/depth8/minleaf20/sqrt/seed42/njobs1, features,20-session targets,.65/.35 thresholds, trend/veto rules,caps,costs and actual176dividends. This is one corpus/coverage-weighting hypothesis, not a model/cadence grid.
- After each head's temporal purge, assign row weight1/(eligible rows on that feature date), normalize to mean1. This gives each observed training date equal total weight despite IPO/delist panel breadth. Record weighted and unweighted prevalence, effective dates/identities/rows, use weighted fitted prevalence as causal Brier reference. Status never controls weights. Defaults without a separate training panel retain exact original outputs/audits.
- Require at least20 distinct added identities with usable pre2018 labeled rows before fitting; otherwise document insufficient panel and stop model fitting without substituting instruments. This is a data-sufficiency gate, not evidence that20identities suffice for generalization.

## Model admission and later evaluation

- Freeze validated panel hashes/source manifests/feature lineage and code/protocol/environment before fitting/returns. First fit/evaluate ONLY2018-2020. Compare broader raw control/dual, same unchanged evaluator. Each admitted arm needs>=20%CAGR,SPY outperformance,DD<original32.681374% and actualMLsales. Dual also needs CAGR>=broadercontrol,DD<broadercontrol,dedicatedMLsales,AUC>.5/Brierbettercausalweightedreference. Report matched five-stock forecast diagnostics and annual panel coverage.
- Persist/hash admissions before later fits/returns. Reject both ->no later work. Control-only ->fit only later uphead. Dual ->paired control/dual later and full, selected25+25bps sensitivity. No post-test selection, threshold/cadence/universe substitution.
- Overall research pass remains>=20%CAGR andSPY outperformance with genuine model sales on BOTH2018-2026full and2021-2026later; fullDD<original32.6814%,laterDD<SPY. Model qualification still does not supply deployable artifacts,inference,promotion/execution/dividend/restart/prospective commissioning.

## Scope / tasks

Root owns frozen sampling/probe/full acquisition under ignored `models/broader-20261112`, this protocol, final report/README/publication. Reuse existing safe transport/cleaning; no production ingestion changes.

Task1 owns new `ml/src/stocklab_ml/training_panel.py` and focused tests: metadata-bound daily input validation, contiguous episodes, complete-month aggregation, existing feature reuse/terminal censoring and explicit row/disposition summaries. No simulated returns or generic universe framework.

Task2 owns optional separate training-frame/date-weight integration in the shared fitter and its wrappers, focused tests, and opt-in broader runner integration reusing existing account/forecast/admission helpers. Coordinate exact interfaces before edits; do not weaken old input/source/calendar guards. Tests must prove different training/prediction symbols, head-specific purges, causal weighted prevalence and exact default parity.

### Compiled panel / runner interface

The root compiler writes a NEW `panel/` directory only after `acquisition-progress.json` is `complete` with256terminal outcomes and no pending identities. `panel/training.csv` contains added identities ONLY, using canonical training symbol `ticker:MIC` after checking cached source symbol `ticker:exchange`. This is a training identity token, not a new tradable broker symbol. Preserve frozen sample MIC keys; do not conflate venue/identity histories.

Canonical CSV columns: date,symbol,ordered DOWNSIDE_FEATURES,target_20,target_date_20,target_price_downside20,target_date_price_downside20. Unknown target/date pairs stay jointly missing. Both labels are binary when known, known dates strictly exceed feature dates; finite ordered features and unique chronological(date,symbol) rows required. Status is absent from features. Full original-five downside frame supplies the same columns and is concatenated only at runtime to this added-only panel.

`panel/manifest.json` fixes schema_version1,protocol`broader-training-20261112-v1`,role`training_only`,price_basis`splits`,monthly_provenance`daily_derived_complete_months`,sample_count256,CSV rows/columns/hash,frozen SPY hash,current feature-source hashes,and `root_files` hashes relative to the acquisition root(selection/protocol/probe/acquisition-progress/acquisition-results,all256source manifests,successful raw/clean files,compiled per-identity reports/attrition). Bind and archive research.py,swing_research.py,downside_research.py,training_panel.py,features/engineering.py and features/models.py, including the shared RSI parameters. Preserve the compiler Python/numpy/pandas/scikit-learn environment in a hash-bound panel/environment.json. Paths must resolve inside that root. The loader verifies full source completion/hash binding,sample identity membership,recomputes the>=20added-pre2018-identity gate and rejects unexpected evaluation symbols or stale feature-source hashes before fitting. It does not infer missing stock identity lifetimes.

Extend the SAME example runner with mutually exclusive `--monthly` and `--training-panel <panel-dir>`. Broad mode refits BOTH heads annually using keyword `training_frame=combined_frame,date_weighted=True`;cached annual entry scores remain contextual only. Reuse strict per-arm20% admission and existing rejection/control-only/dual paths. Broad input protocol is `broader-training-model-20261112-v1`,and copied plan is this file. Bind panel manifest/CSV/source hashes and archive modeling/random_forest.py (the imported RF parameter dictionary) before models. Preserve annual/monthly defaults. No second accounting or new model family.

- [x] Freeze catalog selection/protocol, run six bounded probes and retain all access/identity/coverage outcomes.
- [x] Verify aggregate/native parity; implement/test episode/panel and separate training-frame fitting with red-green checks.
- [ ] Acquire only frozen remaining identities when feasible, validate attrition/coverage and data-sufficiency gate before any model fit.
- [ ] Run one fixed selection, preserve admissions before later evaluation, and report success/failure honestly.
- [ ] Independent source/data/ledger/report review, final relevant checks/secret scan, commit/push and update draftPR290. Overall goal stays active unless fully verified.
