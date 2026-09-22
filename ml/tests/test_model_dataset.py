"""Labels and holdout boundaries must never borrow another symbol or test outcome."""

import numpy as np
import pandas as pd
import pytest

from stocklab_ml.features import engineer_features
from stocklab_ml.features.models import OUTPUT_COLUMNS
from stocklab_ml.modeling import (
    ModelDatasetError, ModelTrainingError, build_supervised_dataset,
    chronological_holdout_split,
)


def feature_frame(closes=None, symbol="AAPL", count=100):
    """Synthetic #60 contract; small literal closes make label cases inspectable."""
    if closes is None:
        prices = 100 + np.arange(count) * 0.07 + 3 * np.sin(np.arange(count) * 0.7)
        raw = pd.DataFrame({
            "date": pd.date_range("2024-01-01", periods=count, freq="B").as_unit("ns"),
            "symbol": pd.array([symbol] * count, dtype="string"),
            "open": prices, "high": prices + 1, "low": prices - 1, "close": prices,
            "volume": pd.array(100 + np.arange(count), dtype="Int64"),
        })
        return engineer_features(raw).dataframe
    frame = feature_frame(symbol=symbol, count=len(closes) + 20)
    for column in ["open", "high", "low", "close"]:
        frame[column] = np.asarray(closes, dtype=float)
    return frame


def test_labels_per_symbol_equal_down_and_last_unobserved():
    frame = pd.concat([feature_frame([100, 102, 101, 101]),
                       feature_frame([200, 199, 201], "MSFT")], ignore_index=True)
    before = frame.copy(deep=True)
    result = build_supervised_dataset(frame)
    assert result.dataframe.target_up_1d.tolist() == [1, 0, 0, 0, 1]
    assert result.dataframe.target_up_1d.dtype == np.dtype("int64")
    assert result.report.unlabeled_rows_removed_per_symbol == {"AAPL": 1, "MSFT": 1}
    assert (result.report.input_rows, result.report.supervised_rows) == (7, 5)
    assert result.dataframe.groupby("symbol").size().to_dict() == {"AAPL": 3, "MSFT": 2}
    pd.testing.assert_frame_equal(frame, before)
    pd.testing.assert_frame_equal(result.dataframe, build_supervised_dataset(frame).dataframe)


def test_next_session_can_cross_weekend_or_missing_dates():
    frame = feature_frame([100, 102, 101])
    frame["date"] = pd.to_datetime(["2024-02-02", "2024-02-05", "2024-02-09"]).as_unit("ns")
    result = build_supervised_dataset(frame).dataframe
    assert result.target_date.tolist() == [pd.Timestamp("2024-02-05"), pd.Timestamp("2024-02-09")]
    assert result.date.tolist() == frame.date.iloc[:2].tolist()


def test_global_unique_date_split_and_purge_with_sparse_symbol():
    aapl = feature_frame([100, 102, 101, 104, 103, 106, 105, 108, 107, 110, 109])
    msft = aapl.iloc[[0, 2, 4, 6, 9, 10]].copy().reset_index(drop=True)
    msft["symbol"] = pd.array(["MSFT"] * len(msft), dtype="string")
    supervised = build_supervised_dataset(pd.concat([aapl, msft], ignore_index=True)).dataframe
    before = supervised.copy(deep=True)
    split = chronological_holdout_split(supervised)
    boundary = aapl.date.iloc[8]
    assert split.report.split_date == boundary.date().isoformat()
    assert split.train.date.max() < boundary
    assert split.train.target_date.max() < boundary
    assert split.test.date.min() >= boundary
    assert split.report.train_rows_per_symbol == {"AAPL": 7, "MSFT": 3}
    assert split.report.test_rows_per_symbol == {"AAPL": 2, "MSFT": 1}
    assert split.report.purged_boundary_rows == 2
    assert split.report.train_rows + split.report.test_rows + 2 == len(supervised)
    assert split.report.train_positive_rate == split.train.target_up_1d.mean()
    assert split.report.test_positive_rate == split.test.target_up_1d.mean()
    repeated = chronological_holdout_split(supervised)
    pd.testing.assert_frame_equal(split.train, repeated.train)
    pd.testing.assert_frame_equal(split.test, repeated.test)
    pd.testing.assert_frame_equal(supervised, before)
    assert split.report == repeated.report


@pytest.mark.parametrize("fraction", [0, 1, -0.1, 1.1, np.nan, np.inf, True, "0.2"])
def test_invalid_test_fraction_fails(fraction):
    with pytest.raises(ModelDatasetError, match="test_fraction"):
        chronological_holdout_split(build_supervised_dataset(feature_frame()).dataframe,
                                    test_fraction=fraction)


def test_fraction_uses_ceiling_of_unique_dates():
    supervised = build_supervised_dataset(feature_frame([100, 102, 101, 104, 103, 106, 105, 108])).dataframe
    split = chronological_holdout_split(supervised, test_fraction=0.3)
    assert split.report.test_rows == 3  # ceil(7 * 0.3), not rows shuffled/rounded down
    assert split.report.train_rows == 3
    assert split.report.purged_boundary_rows == 1


@pytest.mark.parametrize("closes,error,match", [
    ([100], ModelDatasetError, "supervised"),
    ([100, 101], ModelDatasetError, "holdout"),
    ([100, 101, 100], ModelDatasetError, "train"),
    (list(range(100, 120)), ModelTrainingError, "class"),
])
def test_insufficient_rows_empty_partitions_and_single_train_class(closes, error, match):
    with pytest.raises(error, match=match):
        chronological_holdout_split(build_supervised_dataset(feature_frame(closes)).dataframe)


@pytest.mark.parametrize("bad", ["missing", "extra", "nan", "infinity", "dtype", "order", "duplicate", "empty", "raw"])
def test_training_rejects_invalid_feature_contract(bad):
    frame = feature_frame()
    if bad == "missing":
        frame = frame.drop(columns="ma_5")
    elif bad == "extra":
        frame["next_close"] = 999.0
    elif bad == "nan":
        frame.loc[0, "ma_5"] = np.nan
    elif bad == "infinity":
        frame.loc[0, "momentum_10"] = np.inf
    elif bad == "dtype":
        frame["ma_5"] = frame.ma_5.astype(str)
    elif bad == "order":
        frame = frame.iloc[::-1].reset_index(drop=True)
    elif bad == "duplicate":
        frame.loc[1, "date"] = frame.date.iloc[0]
    elif bad == "empty":
        frame = frame.iloc[:0]
    else:
        frame = frame.iloc[:, :7]
    with pytest.raises(ModelDatasetError, match="feature dataset"):
        build_supervised_dataset(frame)


@pytest.mark.parametrize("bad", ["label", "bool", "target_date", "missing", "empty"])
def test_public_split_validates_supervised_contract(bad):
    frame = build_supervised_dataset(feature_frame()).dataframe
    if bad == "label":
        frame.loc[0, "target_up_1d"] = 2
    elif bad == "bool":
        frame["target_up_1d"] = frame.target_up_1d.astype(bool)
    elif bad == "target_date":
        frame.loc[0, "target_date"] = frame.date.iloc[0]
    elif bad == "missing":
        frame = frame[OUTPUT_COLUMNS]
    else:
        frame = frame.iloc[:0]
    with pytest.raises(ModelDatasetError):
        chronological_holdout_split(frame)
