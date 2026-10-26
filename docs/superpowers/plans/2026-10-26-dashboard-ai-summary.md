# Dashboard AI summary implementation plan

**Goal:** Complete #251 using the existing AI performance contract, without changing decisions or trading.

**Spec:** `docs/superpowers/specs/2026-10-26-dashboard-ai-summary-design.md`.

- [x] Write rendered summary/loader tests and observe the missing metric/resource behavior before implementation.
- [x] Add Summary resource group and stable refresh callback to `useAiTraderData`; replace the Dashboard's constant metrics and unused chart with actual return/P&L/win-rate/trade count, explicit states, refresh and UTC provenance. Reuse existing formats/feedback; update FR/EN and scoped responsive CSS.
- [x] Verify session races/expiry, refresh/errors and no unrelated AI reads, then frontend lint/build/full tests. Review independently and fix concrete findings.
- [x] Validate desktop/mobile in Playwright (Browser plugin absent), controlled populated/error states and separate real backend smoke. Document limits without adding sample database activity.
- [ ] Commit/push/open PR to develop, attach it and wait for CI; leave merging and bot commissioning separate.

## Verification ledger

- Observed 7 rendered tests fail because no connected summary existed, then 13 targeted summary/shared-loader checks passed. Added malformed/stale refresh and idle-expiry checks; frontend lint/build and all 459 tests passed.
- Independent review found only a stale Dashboard footer claiming services were not connected; replaced it with the existing paper-portfolio note. Seven existing SSR/no-fake-data checks then passed; final full checks rerun after this text change.
- Browser plugin not available: bundled Playwright used installed headless Chrome. Real local login/API read returned HTTP 200 and zero executed trades. Separate browser-only controlled responses verified populated FR metrics, precise money, manual refresh, 503/404/retry, desktop 1440×1000, mobile 390×844, no horizontal clipping and navigation to AI Trader. No runtime errors or unexpected console warnings/errors. Screenshots use finished animation states; no DB records or bot activation added.
- Final tree after the footer correction: lint, TypeScript/Vite build and all 459 frontend tests passed without failures/skips. Reviewer confirmed P3 resolved and no remaining blocker. Backend/Python implementation is unchanged; CI validates those suites.
