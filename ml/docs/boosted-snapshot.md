# Experimental boosted28 snapshots

The Python module supports genuine fitted HGBT28 snapshots through standalone save/load and the existing version registry/artifact APIs. This is an experimental archive/inference path. The active RFv7 worker is unchanged, and the [financial studies](boosted-exit-controls.md) still do not meet the full20% CAGR/lower-drawdown objective.

## Exact contract

Only `hgb_swing20_market28` /`hist_gradient_boosting` is supported: original-five training,28 ordered swing/lagged-market features, target `open[t+21]/open[t+1]-1 > .002`, four-year strictly purged fitting, date weights and fixed HGBT200 parameters. Annual/monthly cadence is immutable metadata. SPY32, other horizons and disguised RF reports are rejected.

`stocklab_ml.boosted_snapshot.reproduce_snapshot` performs one real fit only when explicitly called. It requires source/frame/population identities, the exact raw core audit and the whole frozen prediction-period vector. Every reproduced score must match exactly, without tolerance, rounding or seed retries. The actual current UTC training timestamp is generated internally, separately from the historical fit boundary.

The snapshot contains `model.joblib` and `snapshot.json`. Loading checks an external manifest reference, strict metadata, runtime, bounded bytes and hashes before deserialization, then the genuine fitted estimator/classes/features/parameters. Source hashes establish byte identity, not historical first-publication availability. Only explicitly trusted private joblib sources may be loaded.

## Existing registered-artifact workflow

Use an explicit **isolated private registry** with a verified `snapshot`, original `feature_rows` and a retained `training_run_id` UUID:

```python
from datetime import datetime
from pathlib import Path
from stocklab_ml.artifacts import load_model_artifact, save_model_artifact
from stocklab_ml.versioning import ModelVersionRegistry

registry = ModelVersionRegistry(Path("models/my-experiment/registry.json"))
record = registry.register_version(
    snapshot, training_run_id=training_run_id,
    trained_at_utc=datetime.fromisoformat(snapshot.metadata["trained_at_utc"]),
)
bound = registry.bind_version(
    snapshot, model_name=record.model_name, model_version=record.model_version,
)
artifact = save_model_artifact(bound, output_dir=Path("models/my-experiment/artifacts"))
loaded = load_model_artifact(artifact, registry=registry, trusted_source=True)
predictions = loaded.predict(feature_rows)
decisions = predictions.policy_decisions(feature_rows).to_dataframe()
```

Registration is Archived with null legacy metrics/holdout. Its typed report contains `snapshot_metadata` and `estimator_sha256`, without a fabricated one-day split/confusion matrix. Identical UUID retries preserve the record; conflicting metadata or training timestamps fail. Unsupported activation and forged Active associations fail before registry/artifact publication. The generic promotion evaluator is unsupported for this target and can retain its normal BLOCKED audit.

Registered publication reuses the exact retained binary bytes. Loading binds their SHA to the registered record before unpickling and retains one verified snapshot; later estimator mutation fails. Predictions preserve genuine `probability_up`. No-argument `historical_predictions()` is rejected because the snapshot stores no legacy prediction table.

The immediate trend200 policy is BUY at p>=.65 with positive MA50, SELL at p<=.35 with negative MA200 and momentum20, otherwise HOLD. A trend-blocked high/low score remains unchanged. The prediction batch binds those three keyed trend inputs; supplying different policy context under an old score fails. Extra unchanged historical context is permitted. Legacy LR/RF confidence behavior remains unchanged.

## Evidence and remaining commissioning

`assess_snapshot` uses this estimator on its original bound frame. Historical assessment selects the latest50 complete known-outcome dates after the fit boundary, five symbols per date and zero fitting-row overlap. Recipe walk-forward forecasts cannot substitute for one snapshot's holdout. A newly reproduced estimator has no prospective observations: its prospective state is PENDING with null metrics. The current helper accepts only the original bound frame; new prospective vintages require subsequent operational support.

The [fixed one-export protocol](../../docs/superpowers/plans/2026-11-12-boosted-snapshot-export.md) is completed and independently audited. Its preregistration bytes remain unchanged; completion evidence is recorded here. At source commit`6133da2`, exactly one real reproduction uses4910 training rows/982 dates and retains a genuine424,480-byte HGBT artifact. All1085 forecast scores across217 sessions in2026 match the frozen annual vector exactly before and after standalone/registered roundtrips. All1085 policy rows agree, including148 high-score trend-blocked HOLDs. These are forecast rows, not newly executed portfolio trades. Original and registered binaries share SHA`11dbea35f440fd811d2adeb6eb974eed9ce3a46f328093af81ec2d017176c458`.

The actual new training time is`2026-11-13T02:01:59.101472+00:00`, distinct from the historical first prediction2026-01-02. The isolated registry contains Archived`v000001`, null legacy metrics/holdout and no Active entry. UUID retries, activation refusals, context substitution and forged Active association checks pass without modifying the original proof. The existing RFv7 registry and two inspected native configs remain byte-identical. The running task's actual configuration was not inspected.

This snapshot's latest50 complete historical dates/250 rows give AUC .57320 and Brier .24123, **worse** than its fitting-prior Brier .23892. There are zero prospective dates, so prospective metrics remain PENDING/null. Neither the recent AUC nor a faithful export qualifies the financial strategy. The auditor performs zero fits/account replays, independently reconstructs all16,345 targets, verifies84 source archives and the51-path old research union. Private evidence is under`models/boosted-snapshot-export-20261112`; independent-audit SHA`46cbc76a61e17abaa17155ec75555260c17d71ae6b09cbbc1659881a0db4cf6b`.

Validation: the full corrected production-source suite passes2183 tests with one Windows skip and five known warnings from older calibration tests. The subsequent test-only restoring thread fixture is verified by all70 new tests with parent OMP unset. Exact-head CI verifies the final committed test harness.

## Read-only application catalog

The later [isolated .NET/Python diagnostic](boosted-diagnostic.md) also invokes this actual Archived artifact, preserving raw probabilities and trend-blocked HOLD semantics. Two real operator outputs pass independent exact comparison, with no fit/replay/provider calls. This is historical reproduction before host construction, not Active selection or commissioned execution.

The backend/frontend catalog now accepts the exact HGBT28 Archived record while retaining strict legacy LR/RF validation and wire shape. HGBT accuracy/precision/recall/F1 and evaluation period are null, not zero. Only seven forecast fields are projected: target name/definition,20-session horizon, ordered28 features, annual/monthly cadence, historical fit boundary and prospective PENDING. Source paths/hashes, raw reports/parameters, dependency inventories and artifact locations are never returned. The UI renders unavailable metrics as an em dash and pending evidence in EN/FR, separately from the actual training timestamp. No activation/retraining controls are added.

The actual isolated export registry passes the real C# catalog service and API response mapper without loading its estimator or starting a host/provider/database. It returns one Archived`v000001` with the exact seven safe fields; registry SHA before/after is`ce99ed924248c38aff59aa27f4928367ebb8062f9acd389bdf7e2845b66cff8e`. Private proof SHA`b0781c6512bd3d8fbce23a5988e5f8f80b4f6aa1a3ab211e0276bb602a25e05a`, under`models/boosted-catalog-20261112/verification`. Local checks pass662 frontend tests,1419 backend tests/one existing skip, lint/build and zero backend build warnings/errors. Final exact-head CI is recorded in the PR.

The [new source-bound target-free preparer](boosted-vintage.md) supplies all28 inputs from an explicit local vintage and reproduces all457,660 frozen feature cells plus1085 genuine Archived-model scores/policy rows without fitting. Actual preparation UTC is recorded separately from historical as-of; original publication/prospective availability remains unproved. Native acquisition/cycle integration still needs to supply these verified vintages. Current-quote paper fills also need next-session, fee/slippage and dividend parity, followed by isolated restart/idempotence proof and prospective observation. The default registry/configuration is unchanged; the actual authenticated UI/worker is not commissioned by this read-only proof. Those requirements and financial qualification remain before full commissioning or a change to the active worker.
