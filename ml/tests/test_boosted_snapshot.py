"""Synthetic snapshot contracts; tests named fitted require explicit CPU release."""

from copy import deepcopy
from dataclasses import FrozenInstanceError, replace
from datetime import UTC, datetime
from importlib import import_module
import hashlib
import io
import json
from pathlib import Path

import numpy as np
import pandas as pd
import pytest


FEATURES = [
    "return_1d", "rsi_14", "momentum_10", "volatility_20", "close_ma5", "close_ma20",
    "ma_ratio", "relative_volume", "monthly_range_9", "monthly_range_12", "monthly_range_19",
    "monthly_rsi", "monthly_multiplier", "momentum_20", "momentum_63", "momentum_126",
    "momentum_252", "close_ma50", "close_ma200", "volatility_63", "drawdown_252",
    "relative_momentum_20", "relative_momentum_63", "relative_momentum_252",
    "vix_level", "vix_change5", "vix_term_ratio", "vix_term_change5",
]
SYMBOLS = ["AAPL", "JNJ", "JPM", "MSFT", "XOM"]


def api():
    return import_module("stocklab_ml.boosted_snapshot")


def digest(frame):
    return hashlib.sha256(frame.to_csv(index=False, date_format="%Y-%m-%d",
        lineterminator="\n").encode()).hexdigest()


@pytest.fixture
def period(tmp_path):
    dates = pd.to_datetime(["2019-12-31", "2020-01-02", "2023-11-20", "2023-11-21",
        "2023-11-22", "2024-01-02", "2024-01-03"]).astype("datetime64[ns]")
    frame = pd.DataFrame([{"date": day, "symbol": symbol} for day in dates for symbol in SYMBOLS])
    for number, name in enumerate(FEATURES):
        frame[name] = .1 + number / 100
    frame["target_20"] = [0., 1., 0., 1., 1.] * 5 + [np.nan] * 10
    frame["target_date_20"] = pd.to_datetime(
        [day for day in ["2020-01-31", "2020-02-03", "2023-12-20", "2024-01-02", "2024-01-03", None, None]
         for _ in SYMBOLS]).astype("datetime64[ns]")
    predictions = frame.loc[frame.date.ge("2024-01-01"), ["date", "symbol"]].reset_index(drop=True)
    predictions["probability_up"] = .6
    audit = {"recipe": "boosted_synthetic_swing20", "label": "target_20", "first_fit": "2020-01-02",
        "last_fit": "2023-11-20", "last_target": "2023-12-20", "first_prediction": "2024-01-02",
        "train_rows": 10, "training_positive_rate": .6, "date_weighted": True,
        "training_unweighted_positive_rate": .6, "training_weighted_positive_rate": .6,
        "training_dates": 2, "training_identities": 5, "training_rows": 10}
    source = tmp_path / "frozen-source.txt"
    source.write_text("synthetic immutable input\n")
    options = {"refit_frequency": "annual", "frame_sha256": digest(frame),
        "prediction_sha256": digest(predictions),
        "sources": {str(source): hashlib.sha256(source.read_bytes()).hexdigest()}}
    return frame, predictions, audit, options


def prepare(period, **changes):
    frame, predictions, audit, options = period
    return api().prepare_period(frame, predictions, audit, **(options | changes))


def test_period_uses_inclusive_four_year_start_and_strict_known_date(period):
    train, rows, weights, metadata = prepare(period)
    assert train.date.drop_duplicates().tolist() == [pd.Timestamp("2020-01-02"), pd.Timestamp("2023-11-20")]
    assert rows.date.drop_duplicates().tolist() == [pd.Timestamp("2024-01-02"), pd.Timestamp("2024-01-03")]
    assert weights.tolist() == [1.] * 10
    assert metadata["fit_audit"] == period[2]
    assert metadata["refit_frequency"] == "annual"
    assert metadata["period_key"] == "2024"
    assert metadata["frame_sha256"] == period[3]["frame_sha256"]


def test_monthly_cutoff_is_first_observed_session_and_does_not_accept_midperiod(period):
    _, _, _, metadata = prepare(period, refit_frequency="monthly")
    assert metadata["period_key"] == "2024-01"
    frame, predictions, audit, options = period
    changed = audit | {"first_prediction": "2024-01-03"}
    with pytest.raises(ValueError):
        api().prepare_period(frame, predictions, changed, **(options | {"refit_frequency": "monthly"}))


def test_uneven_date_population_has_equal_total_date_weight_and_weighted_prior(period):
    frame, predictions, audit, options = period
    frame = frame.loc[~(frame.date.eq("2023-11-20") & frame.symbol.eq("AAPL"))].reset_index(drop=True)
    # Five then four rows: weights .9 and 1.125; date positive rates .6 and .75.
    prior = .6750000000000002  # Core normalization's float64 result, not rounded audit metadata.
    audit = audit | {"train_rows": 9, "training_rows": 9, "training_positive_rate": prior,
        "training_weighted_positive_rate": prior, "training_unweighted_positive_rate": 6 / 9}
    train, _, weights, metadata = api().prepare_period(frame, predictions, audit,
        **(options | {"frame_sha256": digest(frame)}))
    assert weights.tolist() == pytest.approx([.9] * 5 + [1.125] * 4)
    assert weights.groupby(train.date).sum().tolist() == pytest.approx([4.5, 4.5])
    assert metadata["fit_audit"]["training_positive_rate"] == pytest.approx(.675)


@pytest.mark.parametrize("change", [{"training_positive_rate": .61}, {"training_dates": 3},
    {"last_target": "2024-01-02"}, {"label": "target_price_downside20"}, {"date_weighted": False},
    {"forecast_reference_positive_rate": .6}])
def test_changed_or_augmented_core_fit_audit_is_rejected(period, change):
    frame, predictions, audit, options = period
    with pytest.raises(ValueError):
        api().prepare_period(frame, predictions, audit | change, **options)


@pytest.mark.parametrize("bad", ["missing", "duplicate", "nonfinite", "out_of_range", "other_score"])
def test_reference_must_cover_entire_frozen_period_with_real_probabilities(period, bad):
    frame, predictions, audit, options = period
    if bad == "missing": predictions = predictions.iloc[:-1]
    if bad == "duplicate": predictions = pd.concat([predictions, predictions.iloc[:1]], ignore_index=True)
    if bad == "nonfinite": predictions = predictions.assign(probability_up=np.nan)
    if bad == "out_of_range": predictions = predictions.assign(probability_up=1.01)
    if bad == "other_score": predictions = predictions.rename(columns={"probability_up": "confidence"})
    with pytest.raises(ValueError):
        api().prepare_period(frame, predictions, audit,
            **(options | {"prediction_sha256": digest(predictions)}))


@pytest.mark.parametrize("bad", ["frame_digest", "prediction_digest", "source_digest", "changed_source"])
def test_source_and_logical_input_bindings_fail_before_any_fitting(period, bad):
    options = deepcopy(period[3])
    if bad == "frame_digest": options["frame_sha256"] = "0" * 64
    if bad == "prediction_digest": options["prediction_sha256"] = "0" * 64
    if bad == "source_digest": options["sources"] = {next(iter(options["sources"])): "0" * 64}
    if bad == "changed_source":
        from pathlib import Path
        Path(next(iter(options["sources"]))).write_text("changed\n")
    with pytest.raises(ValueError):
        api().prepare_period(*period[:3], **options)


@pytest.mark.parametrize("bad", ["feature", "keys", "label", "known_date", "symbol", "cadence"])
def test_invalid_research_frame_is_not_repaired_or_dropped(period, bad):
    frame, predictions, audit, options = period
    frame, options = frame.copy(), deepcopy(options)
    if bad == "feature": frame.loc[0, FEATURES[0]] = np.inf
    if bad == "keys": frame = pd.concat([frame, frame.iloc[:1]], ignore_index=True)
    if bad == "label": frame.loc[0, "target_20"] = 2.
    if bad == "known_date": frame.loc[0, "target_date_20"] = frame.date.iloc[0]
    if bad == "symbol": frame.loc[0, "symbol"] = "OTHER"
    if bad == "cadence": options["refit_frequency"] = "weekly"
    with pytest.raises(ValueError):
        api().prepare_period(frame, predictions, audit, **(options | {"frame_sha256": digest(frame)}))


def test_unused_future_risk_labels_and_unknown_upside_tails_are_not_feature_filters(period):
    frame, predictions, audit, options = period
    frame = frame.assign(target_price_downside20=np.nan, target_date_price_downside20=pd.NaT)
    train, rows, _, _ = api().prepare_period(frame, predictions, audit,
        **(options | {"frame_sha256": digest(frame)}))
    assert len(train) == 10 and len(rows) == 10
    assert rows.target_20.isna().all()
    assert api().FEATURES == tuple(FEATURES)


@pytest.mark.parametrize("column", [FEATURES[0], "probability_up", "target_20"])
def test_complex_numeric_dtype_is_rejected_without_discarding_imaginary_values(period, column):
    frame, predictions, audit, options = period
    frame, predictions = frame.copy(), predictions.copy()
    target = predictions if column == "probability_up" else frame
    target[column] = target[column].astype("complex128")
    with pytest.raises(ValueError):
        api().prepare_period(frame, predictions, audit,
            **(options | {"frame_sha256": digest(frame), "prediction_sha256": digest(predictions)}))


def test_metadata_cannot_claim_five_identities_from_two_fitted_rows(period):
    module = api()
    metadata = prepare(period)[3] | module._IDENTITY | {"parameters": module._estimator().get_params(),
        "environment": module.runtime_environment(), "trained_at_utc": datetime.now(UTC).isoformat(),
        "omp_num_threads": "1", "evidence": module._EVIDENCE}
    metadata["fit_audit"] = metadata["fit_audit"] | {"train_rows": 2, "training_rows": 2, "training_dates": 1}
    with pytest.raises(ValueError):
        module._metadata(metadata)


def test_policy_preserves_raw_high_and_low_scores_when_trend_requires_hold():
    dates = pd.date_range("2024-01-02", periods=7).astype("datetime64[ns]")
    scores = pd.DataFrame({"date": dates, "symbol": "AAPL",
        "probability_up": [.9, .1, .65, .35, .5, .35, .65]})
    context = scores[["date", "symbol"]].assign(close_ma50=[0., 1., 1., 1., 1., 1., -1.],
        close_ma200=[-1., 0., -1., -1., -1., -1., -1.], momentum_20=[-1., -1., -1., -1., -1., 0., -1.])
    actual = api().policy_decisions(scores, context)
    assert actual.action.tolist() == ["HOLD", "HOLD", "BUY", "SELL", "HOLD", "HOLD", "HOLD"]
    assert actual.probability_up.tolist() == scores.probability_up.tolist()
    assert actual.reason.tolist() == ["buy_trend_failed", "sell_trend_failed", "up_model", "up_model",
        "threshold_hold", "sell_trend_failed", "buy_trend_failed"]
    assert "confidence" not in actual


def test_policy_missing_context_or_duplicate_predictions_fails():
    scores = pd.DataFrame({"date": pd.to_datetime(["2024-01-02"]), "symbol": "AAPL", "probability_up": .8})
    context = scores[["date", "symbol"]].assign(close_ma50=1., close_ma200=-1., momentum_20=-1.)
    for bad_scores, bad_context in [(pd.concat([scores, scores]), context),
            (scores, context.iloc[:0]), (scores, context.assign(momentum_20=np.nan))]:
        with pytest.raises(ValueError):
            api().policy_decisions(bad_scores, bad_context)


@pytest.fixture(scope="module", autouse=True)
def snapshot_thread_limits():
    from threadpoolctl import threadpool_limits

    with pytest.MonkeyPatch.context() as patch:
        patch.setenv("OMP_NUM_THREADS", "1")
        with threadpool_limits(limits=1):
            yield


@pytest.fixture(scope="module")
def fitted_inputs(tmp_path_factory, snapshot_thread_limits):
    # One small synthetic frozen reference fit, never any real market data.
    from stocklab_ml import swing_research as core
    dates = pd.bdate_range("2023-01-02", periods=360).astype("datetime64[ns]")
    frame = pd.DataFrame([{"date": day, "symbol": symbol} for day in dates for symbol in SYMBOLS])
    positions = np.arange(len(frame), dtype=float)
    for number, name in enumerate(FEATURES):
        frame[name] = np.sin(positions / (9 + number) + number)
    frame["target_date_20"] = frame.groupby("symbol").date.shift(-21)
    frame["target_20"] = frame.return_1d.gt(0).astype(float).where(frame.target_date_20.notna())
    predictions, audits = core._walk_forward_predict(frame, "boosted_synthetic_swing20",
        "2024-01-01", "2024-12-31", "boosted", "target_20", "target_date_20", FEATURES,
        "probability_up", training_frame=frame, date_weighted=True)
    assert len(audits) == 1 and predictions.probability_up.nunique() > 2
    source = tmp_path_factory.mktemp("snapshot-synthetic-source") / "frame.csv"
    frame.to_csv(source, index=False, date_format="%Y-%m-%d", lineterminator="\n")
    options = {"refit_frequency": "annual", "frame_sha256": digest(frame),
        "prediction_sha256": digest(predictions),
        "sources": {str(source): hashlib.sha256(source.read_bytes()).hexdigest()}}
    return frame, predictions, audits[0], options


@pytest.fixture(scope="module")
def fitted_snapshot(fitted_inputs):
    before = datetime.now(UTC)
    result = api().reproduce_snapshot(*fitted_inputs[:3], **fitted_inputs[3])
    return result, before, datetime.now(UTC)


def test_fitted_reproduction_keeps_exact_core_scores_audit_and_actual_creation_time(fitted_inputs, fitted_snapshot):
    snapshot, before, after = fitted_snapshot
    frame, reference, audit, _ = fitted_inputs
    actual = snapshot.predict(frame.loc[frame.date.dt.year.eq(2024)].drop(columns=["target_20", "target_date_20"]))
    pd.testing.assert_frame_equal(actual, reference, check_exact=True)
    metadata = snapshot.metadata
    assert before <= datetime.fromisoformat(metadata["trained_at_utc"]) <= after
    assert metadata["fit_audit"] == audit
    assert metadata["model_type"] == "hist_gradient_boosting"
    assert metadata["feature_columns"] == FEATURES
    assert metadata["target_definition"] == "open[t+21]/open[t+1]-1 > .002"
    assert metadata["evidence"]["recipe"] == "frozen_period_parity"
    assert metadata["evidence"]["prospective"] == {"status": "PENDING", "metrics": None}
    metadata["fit_audit"]["recipe"] = "rf_fake"
    assert snapshot.metadata["fit_audit"] == audit
    with pytest.raises(FrozenInstanceError):
        snapshot.extra = "changed"
    with pytest.raises(TypeError):
        api().reproduce_snapshot(*fitted_inputs[:3], **fitted_inputs[3], trained_at_utc="2020-01-01")


def test_fitted_monthly_identity_is_distinct_without_midperiod_refitting(fitted_inputs):
    frame, reference, _, options = fitted_inputs
    from stocklab_ml import swing_research as core
    january, audits = core._walk_forward_predict(frame, "boosted_synthetic_swing20",
        "2024-01-01", "2024-01-31", "boosted", "target_20", "target_date_20", FEATURES,
        "probability_up", refit_frequency="monthly", training_frame=frame, date_weighted=True)
    result = api().reproduce_snapshot(frame, january, audits[0],
        **(options | {"refit_frequency": "monthly", "prediction_sha256": digest(january)}))
    assert result.metadata["period_key"] == "2024-01"
    pd.testing.assert_frame_equal(result.predict(frame.loc[frame.date.between("2024-01-01", "2024-01-31")]),
        january, check_exact=True)


def test_fitted_probability_mismatch_blocks_reproduction_instead_of_rounding(fitted_inputs):
    frame, reference, audit, options = fitted_inputs
    changed = reference.copy()
    changed.loc[0, "probability_up"] = np.nextafter(changed.probability_up.iloc[0], 1.)
    with pytest.raises(ValueError):
        api().reproduce_snapshot(frame, changed, audit,
            **(options | {"prediction_sha256": digest(changed)}))


def test_fitted_uneven_date_sample_weights_reproduce_existing_core(fitted_inputs, tmp_path):
    from stocklab_ml import swing_research as core
    frame = fitted_inputs[0]
    frame = frame.loc[~(frame.date.eq("2023-01-02") & frame.symbol.eq("XOM"))].reset_index(drop=True)
    reference, audits = core._walk_forward_predict(frame, "boosted_synthetic_swing20",
        "2024-01-01", "2024-12-31", "boosted", "target_20", "target_date_20", FEATURES,
        "probability_up", training_frame=frame, date_weighted=True)
    source = tmp_path / "uneven.csv"
    frame.to_csv(source, index=False)
    snapshot = api().reproduce_snapshot(frame, reference, audits[0], refit_frequency="annual",
        frame_sha256=digest(frame), prediction_sha256=digest(reference),
        sources={str(source): hashlib.sha256(source.read_bytes()).hexdigest()})
    pd.testing.assert_frame_equal(snapshot.predict(frame.loc[frame.date.dt.year.eq(2024)]), reference, check_exact=True)
    assert snapshot.metadata["fit_audit"] == audits[0]


def test_fitted_assessment_uses_own_known_holdout_and_keeps_prospective_pending(fitted_inputs, fitted_snapshot):
    snapshot = fitted_snapshot[0]
    frame, reference, _, _ = fitted_inputs
    result = api().assess_snapshot(snapshot, frame, as_of="2024-12-31", evidence_kind="historical_snapshot")
    assert result["status"] == "AVAILABLE" and result["dates"] == 50 and result["rows"] == 250
    observed = reference.merge(frame[["date", "symbol", "target_20", "target_date_20"]], on=["date", "symbol"])
    observed = observed.loc[observed.target_date_20.notna()]
    observed = observed.loc[observed.date.isin(sorted(observed.date.unique())[-50:])]
    assert result["metrics"]["brier"] == float(np.mean((observed.probability_up - observed.target_20) ** 2))
    for kind, cutoff in [("historical_snapshot", "2024-01-03"), ("prospective_snapshot", datetime.now(UTC).date().isoformat())]:
        pending = api().assess_snapshot(snapshot, frame, as_of=cutoff, evidence_kind=kind)
        assert pending["status"] == "PENDING" and pending["metrics"] is None
    with pytest.raises(ValueError):
        api().assess_snapshot(snapshot, frame, as_of="2024-12-31", evidence_kind="recipe_walk_forward")
    assert snapshot.metadata["evidence"]["prospective"]["status"] == "PENDING"


def test_fitted_roundtrip_preserves_target_free_scores_policy_and_no_clobber(fitted_inputs, fitted_snapshot, tmp_path):
    snapshot = fitted_snapshot[0]
    frame = fitted_inputs[0].loc[lambda x: x.date.dt.year.eq(2024)].drop(columns=["target_20", "target_date_20"])
    reference = snapshot.predict(frame)
    artifact = api().save_snapshot(snapshot, tmp_path / "snapshot")
    assert {p.name for p in artifact.directory.iterdir()} == {"model.joblib", "snapshot.json"}
    manifest_bytes = (artifact.directory / "snapshot.json").read_bytes()
    assert hashlib.sha256(manifest_bytes).hexdigest() == artifact.manifest_sha256
    loaded = api().load_snapshot(artifact, trusted_source=True)
    pd.testing.assert_frame_equal(loaded.predict(frame), reference, check_exact=True)
    pd.testing.assert_frame_equal(api().policy_decisions(loaded.predict(frame), frame),
        api().policy_decisions(reference, frame), check_exact=True)
    assert loaded.metadata == snapshot.metadata
    with pytest.raises(api().ModelArtifactError):
        api().save_snapshot(snapshot, artifact.directory)
    assert (artifact.directory / "snapshot.json").read_bytes() == manifest_bytes


def rewrite_manifest(artifact, update):
    path = artifact.directory / "snapshot.json"
    manifest = json.loads(path.read_text())
    update(manifest)
    manifest["metadata_sha256"] = hashlib.sha256(json.dumps(manifest["metadata"], sort_keys=True,
        ensure_ascii=False, allow_nan=False).encode()).hexdigest()
    path.write_text(json.dumps(manifest), encoding="utf-8")
    return replace(artifact, manifest_sha256=hashlib.sha256(path.read_bytes()).hexdigest())


@pytest.mark.parametrize("bad", ["type", "features", "audit_type", "digest", "runtime", "size", "extra", "backdated"])
def test_fitted_bad_metadata_is_rejected_before_unpickling(fitted_snapshot, tmp_path, monkeypatch, bad):
    artifact = api().save_snapshot(fitted_snapshot[0], tmp_path / "snapshot")
    def change(m):
        if bad == "type": m["metadata"]["model_type"] = "random_forest"
        if bad == "features": m["metadata"]["feature_columns"] = FEATURES[::-1]
        if bad == "audit_type": m["metadata"]["fit_audit"]["training_rows"] = True
        if bad == "digest": m["metadata"]["fit_rows_sha256"] = "bad"
        if bad == "runtime": m["metadata"]["environment"]["numpy"] = "0.0.0"
        if bad == "size": m["artifact_size_bytes"] = True
        if bad == "extra": m["unexpected"] = "unsupported"
        if bad == "backdated": m["metadata"]["trained_at_utc"] = "2020-01-01T00:00:00+00:00"
    artifact = rewrite_manifest(artifact, change)
    import joblib
    monkeypatch.setattr(joblib, "load", lambda *a, **k: pytest.fail("Invalid metadata must never unpickle."))
    with pytest.raises(api().ModelArtifactError):
        api().load_snapshot(artifact, trusted_source=True)


def test_fitted_trust_and_changed_bytes_fail_before_unpickling(fitted_snapshot, tmp_path, monkeypatch):
    artifact = api().save_snapshot(fitted_snapshot[0], tmp_path / "snapshot")
    import joblib
    monkeypatch.setattr(joblib, "load", lambda *a, **k: pytest.fail("Untrusted/corrupt bytes must never unpickle."))
    with pytest.raises(api().ModelArtifactError):
        api().load_snapshot(artifact)
    path = artifact.directory / "model.joblib"
    data = path.read_bytes()
    path.write_bytes(bytes([data[0] ^ 1]) + data[1:])
    with pytest.raises(api().ModelArtifactError):
        api().load_snapshot(artifact, trusted_source=True)


def test_fitted_limits_and_traversal_fail_without_publishing(fitted_snapshot, tmp_path):
    path = tmp_path / "limited"
    with pytest.raises(api().ModelArtifactError):
        api().save_snapshot(fitted_snapshot[0], path, max_artifact_bytes=1)
    assert not path.exists()
    with pytest.raises(api().ModelArtifactError):
        api().save_snapshot(fitted_snapshot[0], tmp_path / ".." / "escape")


def test_fitted_wrong_estimator_parameters_cannot_hide_behind_valid_hashes(fitted_snapshot, tmp_path):
    import joblib
    artifact = api().save_snapshot(fitted_snapshot[0], tmp_path / "snapshot")
    model = deepcopy(fitted_snapshot[0]._model)
    model.set_params(max_iter=201)
    stream = io.BytesIO(); joblib.dump(model, stream, protocol=5, compress=0)
    data = stream.getvalue(); (artifact.directory / "model.joblib").write_bytes(data)
    artifact = rewrite_manifest(artifact, lambda m: m.update(
        artifact_size_bytes=len(data), artifact_sha256=hashlib.sha256(data).hexdigest()))
    with pytest.raises(api().ModelArtifactError):
        api().load_snapshot(artifact, trusted_source=True)
