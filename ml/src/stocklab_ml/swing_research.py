"""Offline executable-horizon research, separate from live one-day artifacts."""

import numpy as np
import pandas as pd
import warnings
from sklearn.calibration import CalibratedClassifierCV
from sklearn.ensemble import HistGradientBoostingClassifier, RandomForestClassifier
from sklearn.exceptions import ConvergenceWarning
from sklearn.frozen import FrozenEstimator
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler

from stocklab_ml import research
from stocklab_ml.features import FEATURE_COLUMNS
from stocklab_ml.features.engineering import _validate_input
from stocklab_ml.modeling.logistic import HYPERPARAMETERS as LOGISTIC_PARAMETERS
from stocklab_ml.modeling.random_forest import HYPERPARAMETERS as FOREST_PARAMETERS

SWING_FEATURES = research.RESEARCH_FEATURES + [
    "momentum_20", "momentum_63", "momentum_126", "momentum_252",
    "close_ma50", "close_ma200", "volatility_63", "drawdown_252",
    "relative_momentum_20", "relative_momentum_63", "relative_momentum_252",
]
RECIPES = {
    "rf_daily_v1": ("rf_baseline", "target_up_1d", "target_date_1"),
    "rf_swing5": ("rf", "target_5", "target_date_5"),
    "rf_swing20": ("rf", "target_20", "target_date_20"),
    "boosted_swing20": ("boosted", "target_20", "target_date_20"),
    "logistic_swing20": ("logistic", "target_20", "target_date_20"),
    "boosted_excess20": ("boosted", "target_excess_20", "target_date_20"),
}
CALIBRATED_RECIPES = {
    "rf_calibrated5": ("calibrated_rf", "target_5", "target_date_5"),
    "rf_calibrated20": ("calibrated_rf", "target_20", "target_date_20"),
    "rf_calibrated63": ("calibrated_rf", "target_63", "target_date_63"),
}


def _matching_calendar(prices: pd.DataFrame, spy: pd.DataFrame) -> None:
    _validate_input(prices)
    _validate_input(spy)
    if (set(spy.symbol) != {"SPY"} or any(group.date.tolist() != spy.date.tolist()
            for _, group in prices.groupby("symbol", sort=True))):
        raise ValueError("Stock and SPY observed calendars must match exactly.")


def add_targets(frame: pd.DataFrame, prices: pd.DataFrame, spy: pd.DataFrame) -> pd.DataFrame:
    """Labels only: enter open[t+1], exit open[t+1+h], never capture the entry gap.

    Future targets are kept separate from features. Unobserved outcomes stay NaN;
    latest feature rows remain usable for target-free inference.
    """
    _matching_calendar(prices, spy)
    spy_open = spy.set_index("date").open
    targets = []
    for _, group in prices.groupby("symbol", sort=True):
        labels = group[["date", "symbol"]].copy()
        labels["target_date_1"] = group.date.shift(-1)
        labels["target_up_1d"] = group.close.shift(-1).gt(group.close).astype(float).where(
            labels.target_date_1.notna())
        entry = group.open.shift(-1)
        entry_date = group.date.shift(-1)
        for horizon in (5, 20, 63):
            exit_date = group.date.shift(-(horizon + 1))
            forward = group.open.shift(-(horizon + 1)) / entry - 1
            labels[f"target_date_{horizon}"] = exit_date
            labels[f"forward_return_{horizon}"] = forward
            labels[f"target_{horizon}"] = forward.gt(.002).astype(float).where(exit_date.notna())
            if horizon == 20:
                excess = forward - (exit_date.map(spy_open) / entry_date.map(spy_open) - 1)
                labels["excess_return_20"] = excess
                labels["target_excess_20"] = excess.gt(.002).astype(float).where(exit_date.notna())
        targets.append(labels)
    result = frame.merge(pd.concat(targets, ignore_index=True), on=["date", "symbol"],
                         how="left", validate="one_to_one", indicator=True)
    if not result._merge.eq("both").all():
        raise ValueError("Features contain an unobserved stock session.")
    return result.drop(columns="_merge").sort_values(["date", "symbol"]).reset_index(drop=True)


def swing_features(prices: pd.DataFrame, monthly: pd.DataFrame, spy: pd.DataFrame) -> pd.DataFrame:
    """Trailing indicators only; labels are attached afterwards and never features."""
    _matching_calendar(prices, spy)
    frame = research.research_features(prices, monthly, labeled_only=False)
    reference = spy.set_index("date").close
    derived = []
    for _, group in prices.groupby("symbol", sort=True):
        group = group.copy()
        for window in (20, 63, 126, 252):
            group[f"momentum_{window}"] = group.close / group.close.shift(window) - 1
        for window in (50, 200):
            group[f"close_ma{window}"] = group.close / group.close.rolling(window).mean() - 1
        group["volatility_63"] = group.close.pct_change(fill_method=None).rolling(63).std(ddof=0)
        group["drawdown_252"] = group.close / group.close.rolling(252).max() - 1
        for window in (20, 63, 252):
            reference_return = reference / reference.shift(window) - 1
            group[f"relative_momentum_{window}"] = group[f"momentum_{window}"] - group.date.map(reference_return)
        derived.append(group[["date", "symbol"] + SWING_FEATURES[len(research.RESEARCH_FEATURES):]])
    frame = frame.merge(pd.concat(derived, ignore_index=True), on=["date", "symbol"], validate="one_to_one")
    frame = frame.dropna(subset=SWING_FEATURES)
    if frame.empty or not np.isfinite(frame[SWING_FEATURES].to_numpy(dtype=float)).all():
        raise ValueError("Insufficient or nonfinite swing feature history.")
    return add_targets(frame, prices, spy)


def walk_forward_predict(frame: pd.DataFrame, recipe: str, start: str, end: str, *,
                         refit_frequency: str = "annual", training_frame: pd.DataFrame | None = None,
                         date_weighted: bool = False) -> tuple[pd.DataFrame, list[dict]]:
    """Annual by default; every observed refit boundary purges the full target."""
    specifications = RECIPES | CALIBRATED_RECIPES
    if recipe not in specifications:
        raise ValueError("Unknown frozen swing recipe.")
    kind, label, target_date = specifications[recipe]
    columns = FEATURE_COLUMNS if kind == "rf_baseline" else SWING_FEATURES
    predictions, audits = _walk_forward_predict(frame, recipe, start, end, kind, label, target_date,
        columns, "probability_up", refit_frequency=refit_frequency,
        training_frame=training_frame, date_weighted=date_weighted)
    if training_frame is None and not date_weighted:
        for audit in audits:
            audit.pop("training_positive_rate")
    return predictions, audits


def _walk_forward_predict(frame: pd.DataFrame, recipe: str, start: str, end: str, kind: str,
                          label: str, target_date: str, columns: list[str],
                          score_column: str, *, refit_frequency: str = "annual",
                          training_frame: pd.DataFrame | None = None,
                          date_weighted: bool = False) -> tuple[pd.DataFrame, list[dict]]:
    """Private fitting core shared with the separately named downside target."""
    if refit_frequency not in ("annual", "monthly"):
        raise ValueError("Refit frequency must be annual or monthly.")
    if not isinstance(date_weighted, bool):
        raise ValueError("Date weighting must be a boolean.")
    if date_weighted and kind not in ("rf_baseline", "rf", "boosted", "calibrated_boosted"):
        raise ValueError("Date weighting requires a supported tree recipe.")
    if (frame.duplicated(["date", "symbol"]).any()
            or not np.isfinite(frame[columns].to_numpy(dtype=float)).all()):
        raise ValueError("Unique finite feature rows are required.")
    source = frame if training_frame is None else training_frame
    if training_frame is not None:
        required = ["date", "symbol", label, target_date] + columns
        if (not isinstance(source, pd.DataFrame) or not set(required).issubset(source.columns)
                or source.columns.duplicated().any() or source[["date", "symbol"]].isna().any().any()
                or str(source.date.dtype) != "datetime64[ns]"
                or not source.date.eq(source.date.dt.normalize()).all()
                or source.duplicated(["date", "symbol"]).any()
                or not np.isfinite(source[columns].to_numpy(dtype=float)).all()):
            raise ValueError("Separate training requires unique finite feature rows and observed dates/symbols.")
    test = frame.loc[frame.date.between(start, end)]
    if test.empty:
        raise ValueError("No evaluation rows.")
    predictions, audits = [], []
    periods = test.date.dt.to_period("M") if refit_frequency == "monthly" else test.date.dt.year
    for _, rows in test.groupby(periods, sort=True):
        first = rows.date.min()
        train = source.loc[(source.date >= first - pd.DateOffset(years=4)) & (source.date < first)
                           & (source[target_date] < first) & source[label].notna()]
        if train.empty or set(train[label]) != {0, 1}:
            raise ValueError("A purged training set with both classes is required.")
        reference = {}
        if kind == "calibrated_boosted":
            reference_weights = None
            if date_weighted:
                reference_weights = 1. / train.groupby("date").date.transform("size")
                reference_weights = reference_weights / reference_weights.mean()
            reference = {
                "forecast_reference_positive_rate": float(train[label].mean() if reference_weights is None
                    else np.average(train[label], weights=reference_weights)),
                "forecast_reference_unweighted_positive_rate": float(train[label].mean()),
                "forecast_reference_first_fit": train.date.min().date().isoformat(),
                "forecast_reference_last_fit": train.date.max().date().isoformat(),
                "forecast_reference_last_target": train[target_date].max().date().isoformat(),
                "forecast_reference_rows": len(train), "forecast_reference_dates": int(train.date.nunique()),
                "forecast_reference_identities": int(train.symbol.nunique()),
                "forecast_reference_date_weighted": date_weighted}
        calibration = None
        if kind in ("calibrated_rf", "calibrated_boosted"):
            calibration = train.loc[train.date >= first - pd.DateOffset(years=1)]
            if calibration.empty or set(calibration[label]) != {0, 1}:
                raise ValueError("Separate calibration history needs both classes.")
            boundary = calibration.date.min()
            train = train.loc[(train.date < boundary) & (train[target_date] < boundary)]
            if train.empty or set(train[label]) != {0, 1}:
                raise ValueError("Base fit before calibration needs both classes.")
        if kind in ("rf_baseline", "rf", "calibrated_rf"):
            parameters = FOREST_PARAMETERS if kind == "rf_baseline" else FOREST_PARAMETERS | {
                "max_depth": 8, "min_samples_leaf": 20}
            model = RandomForestClassifier(**parameters)
        elif kind == "logistic":
            model = make_pipeline(StandardScaler(), LogisticRegression(**LOGISTIC_PARAMETERS))
        else:
            model = HistGradientBoostingClassifier(max_iter=200, learning_rate=.05,
                max_leaf_nodes=15, l2_regularization=1., early_stopping=False, random_state=42)
        weights = None
        if date_weighted:
            weights = 1. / train.groupby("date").date.transform("size")
            weights = weights / weights.mean()
        calibration_weights = None
        if kind == "calibrated_boosted" and date_weighted:
            calibration_weights = 1. / calibration.groupby("date").date.transform("size")
            calibration_weights = calibration_weights / calibration_weights.mean()
        with warnings.catch_warnings():
            warnings.simplefilter("error", ConvergenceWarning)
            # Uniform weights preserve the original forest bootstrap RNG path.
            if weights is None or weights.nunique() == 1:
                model.fit(train[columns], train[label].astype("int64"))
            else:
                model.fit(train[columns], train[label].astype("int64"), sample_weight=weights)
            if calibration is not None:
                if kind == "calibrated_boosted":
                    positions = np.arange(len(calibration))
                    # The frozen base cannot fit on calibration rows; score the holdout once.
                    model = CalibratedClassifierCV(FrozenEstimator(model), method="isotonic",
                        cv=[(positions, positions)])
                else:
                    model = CalibratedClassifierCV(FrozenEstimator(model), method="sigmoid")
                if calibration_weights is None or calibration_weights.nunique() == 1:
                    model.fit(calibration[columns], calibration[label].astype("int64"))
                else:
                    model.fit(calibration[columns], calibration[label].astype("int64"), sample_weight=calibration_weights)
        p = model.predict_proba(rows[columns])[:, list(model.classes_).index(1)]
        if not np.isfinite(p).all() or not ((p >= 0) & (p <= 1)).all():
            raise ValueError("Invalid model probabilities.")
        output = rows[["date", "symbol"]].copy()
        output[score_column] = p
        predictions.append(output)
        audit = {"recipe": recipe, "label": label, "first_fit": train.date.min().date().isoformat(),
            "last_fit": train.date.max().date().isoformat(), "last_target": train[target_date].max().date().isoformat(),
            "first_prediction": first.date().isoformat(), "train_rows": len(train),
            "training_positive_rate": float(train[label].mean() if weights is None
                                            else np.average(train[label], weights=weights))}
        if training_frame is not None or date_weighted:
            audit.update(date_weighted=date_weighted, training_unweighted_positive_rate=float(train[label].mean()),
                training_weighted_positive_rate=audit["training_positive_rate"],
                training_dates=int(train.date.nunique()), training_identities=int(train.symbol.nunique()),
                training_rows=len(train))
        if calibration is not None:
            audit.update(base_last_target=audit["last_target"],
                calibration_start=calibration.date.min().date().isoformat(), calibration_rows=len(calibration),
                last_target=calibration[target_date].max().date().isoformat())
        if kind == "calibrated_boosted":
            audit.update(reference)
            audit.update(calibration_method="isotonic", calibration_last_fit=calibration.date.max().date().isoformat(),
                calibration_last_target=audit["last_target"], calibration_dates=int(calibration.date.nunique()),
                calibration_identities=int(calibration.symbol.nunique()), calibration_date_weighted=date_weighted,
                calibration_unweighted_positive_rate=float(calibration[label].mean()),
                calibration_weighted_positive_rate=float(calibration[label].mean() if calibration_weights is None
                    else np.average(calibration[label], weights=calibration_weights)),
                calibration_weight_sum=float(len(calibration) if calibration_weights is None else calibration_weights.sum()),
                calibration_weight_min=float(1. if calibration_weights is None else calibration_weights.min()),
                calibration_weight_max=float(1. if calibration_weights is None else calibration_weights.max()))
        audits.append(audit)
    return pd.concat(predictions, ignore_index=True), audits


def choose_recipe(reports: dict[str, dict]) -> str:
    baseline = reports["rf_daily_v1"]
    eligible = [name for name in reports if name == "rf_daily_v1" or (
        reports[name]["annualized_return"] > baseline["annualized_return"]
        and reports[name]["max_drawdown"] <= baseline["max_drawdown"] + .002)]
    return max(eligible, key=lambda name: (reports[name]["target_met"], reports[name]["annualized_return"]))


def calendar_returns(curve: pd.DataFrame) -> dict[str, float]:
    """Continuous-account returns, including the first session's gap each year."""
    previous = float(curve.total_value.iloc[0])
    result = {}
    for year, group in curve.groupby(curve.date.dt.year, sort=True):
        final = float(group.total_value.iloc[-1])
        result[str(year)] = final / previous - 1
        previous = final
    return result


def filtered_orders(predictions: pd.DataFrame, features: pd.DataFrame, policy: str) -> pd.DataFrame:
    """Fixed causal confirmations; suppress orders without manufacturing confidence."""
    if policy not in ("trend50", "persistent5", "trend200"):
        raise ValueError("Unknown fixed exit-confirmation policy.")
    frame = predictions.merge(features[["date", "symbol", "close_ma50", "close_ma200", "momentum_20"]],
                              on=["date", "symbol"], validate="one_to_one", how="left")
    needed = ["probability_up", "close_ma50", "close_ma200", "momentum_20"]
    if (not np.isfinite(frame[needed].to_numpy(dtype=float)).all()
            or not frame.probability_up.between(0, 1).all()):
        raise ValueError("Every prediction needs matching finite causal context and a score in[0,1].")
    frame = frame.sort_values(["symbol", "date"]).reset_index(drop=True)
    buy, sell = frame.probability_up.ge(.65), frame.probability_up.le(.35)
    if policy == "trend50":
        buy &= frame.close_ma200.gt(0)
        sell &= frame.close_ma50.lt(0)
    elif policy == "trend200":
        buy &= frame.close_ma50.gt(0)
        sell &= frame.close_ma200.lt(0) & frame.momentum_20.lt(0)
    else:
        sell = sell.groupby(frame.symbol).transform(lambda values: values.rolling(5, min_periods=5).sum().eq(5))
    frame["action"] = np.select([buy, sell], ["BUY", "SELL"], default="HOLD")
    frame["allocation"] = 1.
    return frame.loc[frame.action.ne("HOLD"), ["date", "symbol", "action", "allocation"]].sort_values(
        ["date", "symbol"]).reset_index(drop=True)
