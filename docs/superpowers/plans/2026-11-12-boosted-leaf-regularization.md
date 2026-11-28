# Fixed boosted leaf regularization diagnostic

**Goal:** Continue the authorized >20% CAGR research while preserving the audited 18.6259% original-five upside candidate and all older evidence. No runtime or promotion change.

## One structural change

The original-five binary candidate improves returns and ranking, but full Brier .28525 remains worse than its .24584 causal training prior. Test ONLY `min_samples_leaf=100` instead of the installed classifier default 20. At most five rows share a feature date, so 100 rows require at least 20 distinct dates versus four with 20 rows. This reduces tiny leaves fitted to overlapping 20-session labels; it does not make the dates independent or guarantee calibration, useful trading or 20% growth.

Keep original-five AAPL/JNJ/JPM/MSFT/XOM training, exactly 28 swing/lagged-market features, the executable binary 20-session target, nine annual 2018–2026 fits, four-year strict known-date purges and equal total weight per eligible date. HGBT classifier: 200 iterations, .05 learning rate, 15 max leaves, L2=1, early_stopping=False, random_state=42, max_bins=255, OMP1. Explicitly verify that effective estimator parameters differ from the frozen binary classifier ONLY in `min_samples_leaf`. No loss, weighting, threshold, target, feature, cadence, exposure or order-policy change.

Use a small separate private annual fitting loop because the frozen shared core does not expose this parameter. Do not monkeypatch the old constructor, edit the six bound core sources, relax source guards, rebind a panel manifest or add a dependency. Reuse existing exact binary audit, signal, account and independent ledger helpers.

## Fixed accounts and references

Same initial 100,000 USD without later contributions, same observed sessions and 176 gross dividend events through 2026-11-11. Same raw .65/.35 scores and trend200 confirmations, next-open fills, cash and 8dp sizing, 10% purchase/20% symbol execution limits, no shorts/leverage/taxes/interest/final liquidation. Run the same three accounts unconditionally: full2018–2026 at 5+5 bps; same forecasts at 25+25 bps; fresh capital from 2021-01-01. 2026 is partial. No extra fits or winner selection for secondary accounts.

The independently audited original-five binary forecasts SHA `f53007c0ad1de79ba3fe306c31a30c3299c9dd2454f6b9702c7fbf3040e68630` and broader forecasts `9eb037ed4921c45f4b6ee99628d346b7be78321ad7b61ac03174620bc7be4fc7` remain byte-identical and are never refitted. Copy/hash their complete account/input/audit evidence. The preceding cost-net regression study is a separate fixed experiment: preliminary full13.1082% CAGR is unfavorable; preserve it and its pending independent audit rather than hiding it or modifying that strategy.

Before fitting, commit and archive this plan, private driver/helpers, parameter comparison, current imported sources, data/panel/market/reference commitments and environment. Require the original-five binary independent audit to pass with exact input/summary/forecast bindings. Refuse overwriting an existing output under `ml/models/boosted-leaf-regularization-20261112`.

Synthetic checks must cover estimator parameter difference, original-five fit populations/date weights/both classes, strict known-date purge and future-label exclusion, missing/duplicate/nonfinite score coverage, the existing .65/.35+trend decisions and separate economic flags. Export the nine exact fit audits, both causal prior Briers, AUC, raw score tails/annual qualifying signals, trades/positions/cash/dividends/exposure/annual returns and all account metrics. Independent audit performs no fits or producer simulations and reconstructs all ledgers and score-to-order traces.

Report full >20% and SPY/binary-reference outperformance, full DD below the binary reference and genuine sales separately from later >20%, paired outperformance, DD versus SPY and activity. Probability diagnostics use BOTH the actual original-five prior and unchanged broader annual prior. More sales do not by themselves prove effective exits. Every result is published; no minimum-leaf/L2/threshold grid or retuning within this experiment.

This is the THIRD newly specified diagnostic after DCA: training-domain ablation, continuous-price-return strategy, then this leaf ablation. All historical periods have already been inspected; none is a pristine holdout. Existing rejections remain unchanged, and `classification='adaptive_diagnostic'`, `research_qualified=False`, `promotion_authorized=False` throughout. Five survivors, dependent outcomes, current adjusted-price/index vintages and repeated research limit generalization. No prospective 20% guarantee or completed commissioning is implied.

- [x] Prepare/review the private loop and pass synthetic checks; freeze commitments before fitting.
- [x] Execute exactly nine fits and all three fixed accounts without changing references.
- [x] Independently audit and publish all results and limits in the existing draft PR.

Post-study outcome: full16.236981% CAGR /25.992698% DD,236 buys/28 sales; higher costs15.609181%; fresh2021 16.362849% /14.946195% DD. Full Brier .29018 also deteriorates. Complete effective parameters differ ONLY in minimum leaf size; all nine fit/prior dictionaries match the binary parent apart from recipe. Independent review verifies32,539 numeric ledger comparisons with max error0.0; eight synthetic checks pass. The18.625926% original-five reference remains stronger; no >20%, qualification or promotion. Prefit bytes remain archived and committed at c5063f5; this post-study note does not retune them.
