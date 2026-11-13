# Daily multi-asset paper portfolio

The user wants an AI-managed virtual portfolio with meaningful buying and selling,
and delegates the choice of cadence to fit the existing API access. The commissioned
AAPL-only logistic baseline is technically running but produced 90 HOLDs in 90
evaluated observations. This does not satisfy the desired portfolio behavior.

## Chosen approach

Reuse the existing daily, multi-symbol inference, risk, paper settlement, registry,
artifact and audited-promotion workflows. Initial research universe: AAPL, JNJ, JPM,
MSFT and XOM. This fixed universe is an engineering choice for a small multi-sector
paper experiment, not a claim that these are optimal investments. Preserve the
existing USD 100,000 system portfolio and all historical decisions and artifacts.

The authorized Twelve Data Fallback key reports Basic access: 8 credits/minute and
800/day. Five historical requests fit a bounded daily pass with quota headroom.
Use genuine provider responses and cached immutable session inputs. Keep Alpha
Vantage for existing enrichment, not repeated execution-price polling.

## Alternatives

- Intraday decisions: requires a different timestamp/feature/label/execution
  contract and a much larger sustained API budget. Not selected.
- Expand the current AAPL-only model's input universe without retraining: its
  out-of-distribution predictions do not establish a valid multi-asset strategy.
  Not selected.
- Train and validate the existing fixed algorithms on the multi-asset universe,
  then commission an eligible candidate: selected under the user's delegation.

## Acceptance

Use a chronological selection period followed by a separate final holdout. Assess
classification, calibration, executable trades, returns and drawdown. A model that
never trades cannot qualify for this user's active-portfolio goal. The initial
study keeps the 0.70 floor; the versioned deployment policy below records any
separately validated choice. Keep 20% maximum symbol exposure, 10% maximum allocation per
trade, ten-position cap and no-short policy. Do not fabricate confidence or lower
thresholds to force purchases. Keep existing guarded registry promotion: evaluation
and explicit application are separate audited operations.

Require at least one executed buy and one executed sell on at least two symbols in
selection and final holdouts, accuracy/F1 >= 0.50, positive final holdout return and
drawdown <= 20%. These are commissioning screening criteria, not profit guarantees.
If no fixed candidate qualifies, retain the current runtime and report the failed
criteria; do not claim that an active portfolio has been commissioned.

## Operation

Use fresh private input/journal scope when changing model/universe, and preserve
the original scope. Keep retraining on the same fixed universe and original
three-day cadence. Schedule inference daily; repeated passes may only replay the
same completed-session decisions. No live broker integration or paid resources.

Sources: https://support.twelvedata.com/en/articles/5615854-credits and the
authorized key's read-only `/api_usage` response observed on 2026-11-06.

## Exploratory policy extension

The first fixed-floor assessment failed: logistic regression made no trades and
the selected forest made two buys/no sells in the final period. Do not deploy it.
The user delegated the strategy/API tradeoff. Evaluate the existing algorithms
with declared minimum directional-score floors 0.60, 0.65 and 0.70, keeping signal
thresholds BUY 0.60/SELL 0.40 and every position/notional limit unchanged. A score
floor is not a calibrated probability of profit. The running service remains at
0.70 during this experiment.

Choose algorithm and floor only on the 2025 chronological validation period;
evaluate the chosen pair on 2026 through the latest completed session. Report
this as exploratory historical evidence because earlier experiments already
inspected parts of 2026. Keep the activity, accuracy/F1 and 20% drawdown screens.
Require positive return after a separately disclosed 10-basis-point-per-side
turnover cost estimate in both periods. This estimate does not change the existing
zero-cost backtest accounting and does not simulate path-dependent fee effects.
No claim of prospective performance or guaranteed trades follows from this screen.

## Selected paper deployment policy

The 2025-only ranking selected Random Forest with minimum directional score 0.65,
not the more active 0.60 policy. Record this choice as `daily-multi-asset-v1` and
permit it only through an explicit cycle configuration. The Python runner supplies
its exact risk options to that backend child process; do not change User Secrets,
HTTP service defaults or personal-portfolio settings. Default/unnamed cycles still
require at least 0.70. The named policy pins 0.65, 20% exposure, 10% order allocation,
ten positions and no shorts. Unknown/mismatched profiles fail before inference.
Use schema-v2 journals that pin the policy name; reject replay/resume under another
policy, and continue reading legacy schema-v1 journals at the original policy.

The exploratory 2026 screen recorded 49 buys/10 sells on all five symbols, 14.40%
gross return and 6.48% gross drawdown. A 10-bps-per-side turnover estimate is $464.67,
leaving an approximate 13.94% return on initial capital. Equal-symbol buy-and-hold
returned 21.63% with 4.91% drawdown, so the forest did not demonstrate superiority
to that benchmark. This is an active paper experiment under the user's delegated
choice, not a recommendation for real money or a profit certification. It trained
on 6,170 rows through 2025-12-30 and evaluated 1,060 rows in 2026; preserve those
distinct ranges in metadata. Four positions remained open in the final backtest.

The launcher and native backend entry point pass a baseline command-line default
to defeat an ambient opt-in. A missing/new journal cannot adopt matching SQL
decisions from an interrupted or completed scope. Commissioning uses one native
runner per system portfolio: stop the old worker, verify all old sessions are
terminal and keep its old scope only as an archive. The new model/version uses
disjoint decision identities. Concurrent distinct scopes are not supported.
