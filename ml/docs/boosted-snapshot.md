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

The backend/frontend catalog still needs a closed target-aware HGBT branch and truthful pending metrics. Native preparation/inference must supply all28 features and verified data vintages. Current-quote paper fills also need next-session, fee/slippage and dividend parity, followed by isolated restart/idempotence proof and prospective observation. Those requirements and financial qualification remain before full commissioning or a change to the active worker.
