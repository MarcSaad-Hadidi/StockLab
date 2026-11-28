# Original-five upside calibration diagnostic

The fixed upside-only isotonic experiment does not meet the user's >20% CAGR/lower-drawdown objective. Full-period return rises to19.2814%, but maximum drawdown worsens to32.5747%; fresh2021 capital underperforms SPY. The lower-risk18.6259%/24.7658% annual28 reference remains retained. RFv7 stays selected in the existing local configuration.

## Fixed method

The [preregistered protocol](../../docs/superpowers/plans/2026-11-12-boosted-upside-calibration.md), committed before fitting as`67fe318`, uses the existing calibrated_boosted branch on the original-five upside target and28 inputs. The earlier published isotonic trial calibrated a different broader32-feature downside head; it did not test this learner/population.

For each actual first annual session2018–2026, form the same four-year eligible population with target-known date strictly before the prediction boundary. Reserve its latest one calendar year by feature date for calibration. Base fitting also purges targets at the actual first calibration session; both partitions require two classes and separately normalized equal-total-date weights. The frozen base is never refitted on the calibration/combined history. The experiment changes BOTH base-history length and probability mapping, so it does not isolate mapping alone.

The unchanged HGBT parameters, original28 features, open[t+1]→open[t+21] price-up target>.002, raw .65/.35 thresholds, trend200 conditions and position limits remain fixed. Instrumented actual calls are nine base HGBT fits, nine frozen-estimator isotonic fits and six stock-account replays. The budget guard preserves estimator method signatures and refuses an excess call before execution. SPY and old reference accounts are copied from their frozen evidence, never refitted/replayed.

All accounts start with100,000 USD/no later deposits and use the same observed sessions through2026-11-11, actual next opens,176 verified gross dividends,8-decimal quantities, pooled cash,10% purchase/20% per-symbol purchase caps and costs below. There are no shorts, leverage, taxes, cash interest or hypothetical final liquidation.2026 is partial; SPY is an adjusted ETF total-return proxy, not the exact S&P500 Total Return Index.

## Complete account results

|Account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Full2018–2026,5+5bps|19.2814%|32.5747%|476,897.35 USD|90 /1|
|Same forecasts, SELL disabled|19.5322%|32.5747%|485,853.42 USD|92 /0|
|Full,25+25bps|19.2076%|32.5733%|474,289.74 USD|97 /1|
|Same forecasts, SELL disabled,25+25bps|19.4688%|32.5733%|483,575.98 USD|99 /0|
|Fresh2021,5+5bps|13.7676%|15.7323%|212,921.90 USD|49 /1|
|Same forecasts, SELL disabled, fresh2021|14.9066%|16.3644%|225,719.43 USD|49 /0|

Full enabled return improves .6555 percentage point versus the retained raw annual28 model, but drawdown worsens7.8089 points. Fresh return loses3.8102 points versus raw17.5778%, and trails matched SPY14.2527%. Neither full nor fresh enabled return exceeds20%. One executed sale is genuine; its existence does not establish better exit behavior.

The matched enabled/no-SELL full accounts differ by−8,956.07 USD/−.2508 CAGR point with ZERO maximum-drawdown reduction. At higher costs the differences are−9,286.24 USD/−.2612 point, again zero drawdown reduction. Fresh sales reduce wealth12,797.53 USD/1.1390 CAGR points while reducing drawdown .6321 point. These isolate permission to sell at account-policy level, not individual-sale alpha. Cross-model comparisons change BUY and SELL forecasts jointly.

Mean investment is96.67% full enabled versus99.24% no-SELL, and72.01% fresh enabled versus79.65% no-SELL. Enabled full fees are96.80 USD versus66.39 USD no-SELL. Higher exposure and lower turnover than the raw annual model help explain the changed full risk/return profile; they do not demonstrate forecasting skill. Disabled controls remain controls and cannot replace the genuine-sales objective.

## Forecast quality and independent references

All11,140 forecasts and105 unknown-label tails are retained; quality uses11,035 mature full outcomes and7,255 later outcomes. Full/later priors are mapped from each causal annual fit, never from a favorable pooled evaluation prevalence.

|Known-outcome diagnostic|Full|Later|
|---|---:|---:|
|Calibrated Brier|.26917|.26292|
|Raw annual28 Brier|.28525|.27793|
|Unchanged full-four-year original-five prior Brier|.24584|.24759|
|Actual purged base-fit prior Brier|.24549|.24828|
|Calibration-holdout prior Brier|.25285|.24964|
|Unchanged broader annual prior Brier|.25065|.25213|
|Calibrated AUC|.48644|.52255|
|Raw annual28 AUC|.51954|.52879|

Brier improves versus the raw model but still loses to every separately labelled prior reference. Full AUC falls below .5. Isotonic produces scores spanning0–1 with ties; approximately230 correlated calibration dates per year do not create1,150 independent observations. Neither exact reproduction nor a smaller Brier alone proves calibrated prospective confidence.

## Verification and operating limits

Eleven synthetic self-checks pass, including preservation of wrapped estimator signatures. Their observed three miniature HGBT fits/three calibrations/one synthetic account are separate from historical execution. The historical producer completes once with observed9/9/6 calls and byte-identical protected RFv7 registry/two inspected native configs.

Independent historical audit passes **251,937 numeric ledger/sidecar comparisons with maximum error0.0**, with zero auditor fits, producer simulations or reference-account replays. It verifies nine exact full/base/calibration populations and87,695 membership rows, four distinct priors, full/later/nine annual forecast diagnostics, six complete financial ledgers/sidecars,48 financial annual-return rows, nine effects,117+245 immutable reference files and53-source/84-snapshot bindings. One checker-only annual-forecast CSV column-order mismatch was corrected; the original checker/failure evidence is preserved, all named values already matched, and no financial value, producer output or strategy was changed/replayed.

Private evidence is under`models/boosted-upside-calibration-20261112`. Driver SHA`49ee67d18d4ba2a12a97d7c375be47ff75f38a4a6a984f1c770d3166ec89e90c`; helper SHA`a2956670134d15727bf53005d9a04b1afbec785e0e9cf55dc4bb934eb6dc2cb3`; independent-audit SHA`fef31341390ec58851dd3fdd3b1f86945eecefe46d9d9956880b4fece432134e`. The preregistration stays unchanged; this report records completed/unfavorable outcomes.

All history has already been examined adaptively. Five selected survivors, overlapping outcomes, current adjusted-price/index vintages and repeated experimentation limit generalization. The full/later20% and joint risk/outperformance objective is unmet; `research_qualified=False` and `promotion_authorized=False`. Native feature preparation, prospective evidence, execution parity and restart commissioning remain separate requirements. No current worker, task, database, broker or DCA Site is changed by this diagnostic.
