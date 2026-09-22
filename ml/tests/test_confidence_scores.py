"""Decision alignment, upstream contract integrity, and label-free pipelines."""

from dataclasses import asdict, replace

import numpy as np
import pandas as pd
import pytest

from stocklab_ml import signals as api
from test_trading_signals import predictions


def signal_result(probabilities=(0.75, 0.50, 0.20), **kwargs):
    return api.generate_trading_signals(predictions(probabilities), model_name="future_model", **kwargs)


@pytest.mark.parametrize("probability,signal,confidence", [
    (1.00, "BUY", 1.00), (0.99, "BUY", 0.99), (0.80, "BUY", 0.80),
    (0.75, "BUY", 0.75), (0.60, "BUY", 0.60),
    (0.59, "HOLD", 0.82), (0.55, "HOLD", 0.90), (0.50, "HOLD", 1.00),
    (0.45, "HOLD", 0.90), (0.41, "HOLD", 0.82),
    (0.40, "SELL", 0.60), (0.25, "SELL", 0.75), (0.20, "SELL", 0.80),
    (0.01, "SELL", 0.99), (0.00, "SELL", 1.00),
])
def test_exact_requested_examples(probability, signal, confidence):
    result = api.add_confidence_scores(signal_result([probability]))
    assert result.signals.signal.tolist() == [signal]
    # Decimal examples allow only floating-point representation error.
    assert result.signals.confidence.iloc[0] == pytest.approx(confidence, abs=1e-15, rel=0)
    assert result.signals.probability_up.tolist() == [probability]


def test_contract_report_determinism_and_deep_immutability():
    source = signal_result()
    # Preserve arbitrary row order and index, including repeated index labels.
    source.signals.index = [7, 7, 2]
    source = replace(source, signals=source.signals.iloc[::-1])
    frame_before, report_before = source.signals.copy(deep=True), asdict(source.report)
    result = api.add_confidence_scores(source)
    assert isinstance(result, api.ConfidenceSignalsResult)
    assert isinstance(result.report, api.ConfidenceScoresReport)
    assert list(result.signals) == [
        "date", "symbol", "model_name", "predicted_class", "probability_up", "signal", "confidence",
    ]
    assert [str(dtype) for dtype in result.signals.dtypes] == [
        "datetime64[ns]", "string", "string", "int64", "float64", "string", "float64",
    ]
    pd.testing.assert_frame_equal(result.signals.drop(columns="confidence"), frame_before, check_exact=True)
    assert asdict(result.report) == report_before | {
        "confidence_min": 0.75, "confidence_max": 1.0, "confidence_mean": 0.85,
    }
    repeated = api.add_confidence_scores(source)
    pd.testing.assert_frame_equal(result.signals, repeated.signals, check_exact=True)
    assert result.report == repeated.report
    result.signals.iloc[0, 0] = pd.Timestamp("2030-01-01")
    result.report.symbols.append("MSFT")
    pd.testing.assert_frame_equal(source.signals, frame_before, check_exact=True)
    assert asdict(source.report) == report_before


def test_enriched_report_cannot_masquerade_as_upstream_result():
    source = signal_result()
    enriched = api.add_confidence_scores(source)
    with pytest.raises(api.ConfidenceScoreError):
        api.add_confidence_scores(replace(source, report=enriched.report))


@pytest.mark.parametrize("value", [-0.01, 1.01, np.nan, np.inf, -np.inf])
def test_invalid_formula_output_raises_instead_of_clipping(monkeypatch, value):
    from stocklab_ml.signals import confidence

    monkeypatch.setattr(confidence, "_confidence_values", lambda frame: np.full(len(frame), value))
    with pytest.raises(api.ConfidenceScoreError, match="confidence"):
        api.add_confidence_scores(signal_result())


@pytest.mark.parametrize("p", [0.0, 0.01, 0.20, 0.40, 0.41, 0.45, 0.50])
def test_symmetry(p):
    result = api.add_confidence_scores(signal_result([p, 1 - p]))
    assert result.signals.confidence.iloc[0] == pytest.approx(result.signals.confidence.iloc[1], abs=1e-15)


def test_custom_thresholds_come_from_report_and_do_not_rescale_confidence():
    source = signal_result([0.70, 0.65, 0.50, 0.35, 0.30], buy_threshold=0.70, sell_threshold=0.30)
    result = api.add_confidence_scores(source)
    assert result.signals.signal.tolist() == ["BUY", "HOLD", "HOLD", "HOLD", "SELL"]
    assert result.signals.confidence.tolist() == pytest.approx([0.70, 0.70, 1.0, 0.70, 0.70])
    assert result.report.buy_threshold == 0.70 and result.report.sell_threshold == 0.30


def test_zero_boundary_and_low_scores_are_retained_without_risk_decisions():
    # Smallest positive float is HOLD under (0,1); the V1 formula rounds to 0.
    source = signal_result([np.nextafter(0.0, 1.0), 0.50, 1.0], buy_threshold=1, sell_threshold=0)
    result = api.add_confidence_scores(source)
    assert result.signals.confidence.tolist() == [0.0, 1.0, 1.0]
    low = api.add_confidence_scores(signal_result([0.01], buy_threshold=0.01, sell_threshold=0))
    assert low.signals.signal.tolist() == ["BUY"]
    assert low.signals.confidence.tolist() == [0.01]
    assert result.report.input_rows == result.report.output_rows == 3


@pytest.mark.parametrize("p,wrong_signal", [(0.80, "HOLD"), (0.20, "BUY"), (0.50, "SELL"),
                                              (0.60, "HOLD"), (0.40, "HOLD")])
def test_signal_probability_mismatch_fails_without_repair(p, wrong_signal):
    source = signal_result([p])
    source.signals.loc[0, "signal"] = wrong_signal
    # Also update counts, so coherence itself must be checked.
    source = replace(source, report=replace(source.report, buy_count=int(wrong_signal == "BUY"),
                     sell_count=int(wrong_signal == "SELL"), hold_count=int(wrong_signal == "HOLD")))
    before = source.signals.copy(deep=True)
    with pytest.raises(api.ConfidenceScoreError, match="coheren|probability"):
        api.add_confidence_scores(source)
    pd.testing.assert_frame_equal(source.signals, before, check_exact=True)


@pytest.mark.parametrize("field,value", [
    ("model_name", "other_model"), ("model_name", "../model"), ("model_name", None),
    ("input_rows", 2), ("output_rows", 4), ("buy_count", 0), ("sell_count", 2), ("hold_count", 0),
    ("buy_count", True), ("input_rows", 3.0), ("hold_count", "1"), ("output_rows", None),
    ("buy_threshold", 0.5), ("buy_threshold", 1.1), ("sell_threshold", -0.1),
    ("buy_threshold", 0.4), ("sell_threshold", 0.8), ("buy_threshold", np.nan),
    ("sell_threshold", np.inf), ("buy_threshold", True), ("sell_threshold", "0.4"),
    ("buy_threshold", None), ("buy_threshold", 0.6 + 0j),
    ("symbols", ["MSFT"]), ("symbols", None), ("date_min", "2020-01-01"), ("date_max", None),
])
def test_malformed_reports_are_rejected(field, value):
    source = signal_result()
    with pytest.raises(api.ConfidenceScoreError):
        api.add_confidence_scores(replace(source, report=replace(source.report, **{field: value})))


@pytest.mark.parametrize("field,value", [
    ("probability_up", -0.01), ("probability_up", 1.01), ("probability_up", np.nan),
    ("probability_up", np.inf), ("probability_up", -np.inf), ("probability_up", True),
    ("probability_up", "0.5"), ("probability_up", None), ("probability_up", 0.5 + 0j),
    ("signal", "WAIT"), ("signal", "buy"), ("signal", None), ("signal", True),
    ("model_name", "other"), ("model_name", None), ("symbol", "aapl"), ("symbol", None),
    ("date", pd.NaT), ("date", "2024-01-01"), ("predicted_class", True), ("predicted_class", 2),
])
def test_invalid_rows_are_rejected(field, value):
    source = signal_result()
    source.signals[field] = value
    with pytest.raises(api.ConfidenceScoreError):
        api.add_confidence_scores(source)


@pytest.mark.parametrize("bad", ["result", "report", "frame", "empty", "missing", "extra", "duplicate_column",
                                  "duplicate_decision", "column_order", "dtype"])
def test_invalid_result_schema_and_unique_keys(bad):
    source = signal_result()
    frame = source.signals
    if bad == "result":
        source = {}
    elif bad == "report":
        source = replace(source, report={})
    elif bad == "frame":
        source = replace(source, signals=[])
    elif bad == "empty":
        source = replace(source, signals=frame.iloc[:0])
    elif bad == "missing":
        source = replace(source, signals=frame.drop(columns="signal"))
    elif bad == "extra":
        source = replace(source, signals=frame.assign(actual_class=1))
    elif bad == "duplicate_column":
        source = replace(source, signals=pd.concat([frame, frame[["signal"]]], axis=1))
    elif bad == "duplicate_decision":
        source = replace(source, signals=pd.concat([frame, frame.iloc[:1]]))
    elif bad == "column_order":
        source = replace(source, signals=frame.loc[:, list(frame)[::-1]])
    else:
        source = replace(source, signals=frame.astype({"predicted_class": "int32"}))
    with pytest.raises(api.ConfidenceScoreError):
        api.add_confidence_scores(source)


def test_batch_symbols_and_audit_class_do_not_affect_other_decisions():
    frame = predictions()
    frame["symbol"] = pd.array(["AAPL", "MSFT", "NVDA"], dtype="string")
    first = api.add_confidence_scores(api.generate_trading_signals(frame, model_name="m"))
    frame["predicted_class"] = [0, 0, 0]
    frame.loc[1, "probability_up"] = 0.8
    second = api.add_confidence_scores(api.generate_trading_signals(frame, model_name="m"))
    assert first.signals.confidence.tolist() == [0.75, 1.0, 0.80]
    assert second.signals.confidence.tolist() == [0.75, 0.80, 0.80]
    assert first.report.symbols == ["AAPL", "MSFT", "NVDA"]


def test_future_labels_never_affect_confidence():
    frame = predictions()
    expected = api.add_confidence_scores(api.generate_trading_signals(frame, model_name="m"))
    for actual, target in [(0, "2025-01-01"), (1, "2030-01-01"), (object(), object())]:
        historical = frame.assign(actual_class=actual, target_date=target)
        result = api.add_confidence_scores(api.generate_trading_signals(historical, model_name="m"))
        pd.testing.assert_frame_equal(result.signals, expected.signals, check_exact=True)
        assert result.report == expected.report
    for extra in ["actual_class", "target_date", "future_close", "future_return"]:
        source = signal_result()
        with pytest.raises(api.ConfidenceScoreError):
            api.add_confidence_scores(replace(source, signals=source.signals.assign(**{extra: object()})))


def test_real_logistic_and_random_forest_target_free_pipelines(monkeypatch):
    from sklearn.ensemble import RandomForestClassifier
    from sklearn.linear_model import LogisticRegression
    from stocklab_ml.modeling import predict_direction, predict_random_forest_direction, train_random_forest
    from test_model_dataset import feature_frame

    features = feature_frame()
    forest = train_random_forest(features)
    logistic = forest.baseline_result
    futures = [predict_direction(logistic.pipeline, features), predict_random_forest_direction(forest.model, features)]

    def forbidden(*args, **kwargs):
        raise AssertionError("Confidence must not call a model or regenerate signals")

    batches = []
    for model, future in zip([logistic, forest], futures, strict=True):
        assert "actual_class" not in future and "target_date" not in future
        for frame in [model.predictions, future]:
            batches.append(api.generate_trading_signals(frame, model_name=model.report.model_name))
    for estimator in [LogisticRegression, RandomForestClassifier]:
        for method in ["fit", "predict", "predict_proba"]:
            monkeypatch.setattr(estimator, method, forbidden)
    from stocklab_ml.signals import generator
    monkeypatch.setattr(generator, "generate_trading_signals", forbidden)
    monkeypatch.setattr(api, "generate_trading_signals", forbidden)
    for source in batches:
        result = api.add_confidence_scores(source)
        assert result.report.input_rows == result.report.output_rows == len(source.signals)
        pd.testing.assert_frame_equal(result.signals.drop(columns="confidence"), source.signals, check_exact=True)
        assert np.isfinite(result.signals.confidence).all()
        assert result.signals.confidence.between(0, 1).all()
        for row in result.signals.itertuples():
            if row.signal == "BUY":
                assert row.confidence == row.probability_up
            elif row.signal == "SELL":
                assert row.confidence == 1 - row.probability_up
            else:
                assert row.confidence == pytest.approx(2 * min(row.probability_up, 1 - row.probability_up), abs=1e-15)
