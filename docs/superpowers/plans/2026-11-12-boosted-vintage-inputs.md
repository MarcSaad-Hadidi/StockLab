# Boosted28 target-free vintage preparation implementation plan

> **For agentic workers:** Use the existing native implementation/review workflow, with failing tests before production code and independent actual-artifact parity review.

**Goal:** Supply the actual28 causal inputs needed for local boosted inference, advancing the user's full ML completion objective without claiming financial qualification or activating an unqualified model.

**Architecture:** One new read-only Python module consumes a closed, externally hash-bound local CSV bundle. Reuse the existing13-feature target-free builder, preserve the exact11 swing trailing formulas and four lagged market formulas, then pass its result to existing Archived artifact prediction/policy APIs. No old research/snapshot/artifact code changes.

**Tech stack:** Existing Python/pandas/NumPy/standard-library validation and artifact helpers; no new dependencies or provider calls.

**Spec:** `ml/docs/boosted-snapshot.md` remaining native28-feature/vintage requirement; the user has delegated implementation choices and requested continued local ML work.

## Decision and scope

Current local preparation supplies only seven baseline features. The HGBT28 artifact already supports target-free prediction and context-bound decisions; its experimental activation remains forbidden. Calling the old research feature wrapper would attach future targets, and the old macro source loader fixes the research cutoff to2026-11-11. Do not alter either bound source. An independent new preparer is the smallest complete input stage; acquisition, native scheduler/backend integration and next-open/cost/dividend commissioning remain later requirements, not silently declared complete here.

Create `ml/src/stocklab_ml/boosted_vintage.py`, `ml/tests/test_boosted_vintage.py` and `ml/docs/boosted-vintage.md`. Link from the existing snapshot guide/README after verification. No changes to package exports, dependency files, existing fixtures/configs,51 original closure paths, the larger53-path five-study union, or84 bound snapshot sources. Native backend/operational callers retain their existing contract in this step.

## Input and public contract

`prepare_vintage(bundle_dir: Path, *, manifest_sha256: str, as_of_utc: datetime) -> tuple[pd.DataFrame, dict]` reads only ordinary bounded files under one real directory. The caller supplies the expected SHA256 of `manifest.json`; strict duplicate-key/finite JSON/schema validation binds exactly `stocks.csv`, `monthly.csv`, `spy.csv`, `vix.csv`, `vix3m.csv`, `expected-sessions.csv` by bytes and SHA256. Reject missing, extra manifest fields/files, traversal, links/junctions, bad hashes/schema and excessive sizes. Reuse existing bounded-read/strict-cleaned CSV validation patterns where possible.

The manifest records exact original-five/SPY identities, split-adjusted stocks/monthly versus all-adjusted SPY, native monthly interval, index-level volatility units, complete daily/monthly recursive prefix starts and independently supplied calendar coverage. These are explicit caller attestations bound to bytes, not certification of original publication, corporate-action vintage or official calendar identity. Never fabricate a retrieval timestamp. Metadata uses actual internally generated `prepared_at_utc`, distinct from supplied `as_of_utc`, and states these limits.

Require aware UTC `as_of_utc` no later than the real clock. The decision cutoff is the last expected session strictly before its UTC date; the independent calendar's declared coverage must include the as-of date. Reject a stale/incomplete calendar, weekends/duplicate/unsorted session keys and any later OHLCV rows. Stock/SPY histories must cover precisely the same expected sessions from their declared daily prefix through cutoff, include all five stock identities/SPY, and end at cutoff. No filling, sorting repair or silently dropped invalid rows. Completeness is relative to the supplied attested calendar; it does not independently prove an official session list.

Monthly input must contain native consecutive month-start candles for all five from the declared complete prefix, only fully completed months, ending exactly at the calendar month before cutoff. A partial current month is rejected. Daily and monthly RSI use the entire supplied, explicitly attested seed history;252 bars/four years cannot replace the recursive prefix. Incomplete/stale/gapped context fails instead of carrying old values.

Volatility input uses strict positive finite dated OHLC and original source order. Retain/index on the actual SPY calendar BEFORE lagging; index-only dates cannot become trading sessions. Require every necessary calendar observation, no filling and no future source bars. Preserve the research availability floor2013-11-01 and exact shifts2/7: VIX/100,5-session change, VIX/VIX3M−1 term ratio and its5-session difference.

Return only ordered `date,symbol` plus the exact28 model features, sorted and finite, with no targets, labels or other columns. Preserve every eligible trailing row and require the latest cutoff's five inputs. Metadata binds normalized source files/manifest, independent calendar, exact frame digest, full prefixes, cutoff, actual preparation UTC, formula schema and dates used for monthly/macro context. No provider, fit, registry, state, database, schedule, broker or environment mutation.

## Tasks and checks

- [ ] Write/run failing tests for the missing API, valid new local bundle and exact target-free matrix; implement only the module above.
- [ ] Cover strict schema/hash/path/size/identity validation, both UTC bounds, current/future daily or monthly bars, stale/missing/duplicate calendar and required sessions, recursive prefix changes, insufficient warm-up, missing/nonfinite macro observations and index-only-date-before-lag handling. Assert input bytes/frames and operational files unchanged.
- [ ] Verify synthetic formula parity and prefix invariance against independent expected values and the existing research implementation without calling any target builder from production code. Run new tests and adjacent snapshot/registry suites, followed by the required full ML suite once final code is frozen.
- [ ] Commit the public module/tests/protocol, then execute one private zero-fit actual-data proof. Its derived normalized bundle must retain source transformation/hash evidence and a calendar adequate for the explicit historical as-of; do not invent historical retrieval times. Compare ALL eligible28-feature cells and the entire1085-score/217-session2026 vector and actions through the genuine Archived loaded artifact. Preserve the original binary/registry/proofs and distinguish this historical reproduction from prospective data.
- [ ] Independent review checks trust boundaries, causal formulas, seed history, complete matrix/score/context parity and immutable53/84 source closures. Publish evidence/limitations in docs and the existing draft PR; no Active change or completion claim.

## Review focus

1. A missing SPY day can corrupt every lag: verify all normalized daily data against the separate attested calendar, while disclosing the calendar's upstream trust limit.
2. A monthly candle dated at its first day can still be incomplete: require the last fully completed month and strict as-of joins.
3. Wilder RSI cannot be reconstructed from a moving short window: retain full declared prefixes and record their exact hashes/starts.
4. Two market lags are not proof of historical availability: actual preparation time and current-vintage limitations remain explicit, and no prospective score claim follows.
5. Matching score values with substituted trend context is insufficient: invoke existing version-bound policy on exactly the same prepared frame and compare every action/reason.

The >20%/lower-drawdown financial objective remains unchanged and unmet until separately demonstrated. This preparer enables real inputs; it does not qualify/promote a strategy or commission the authenticated UI/worker.
