"""Comparable reports only; stored training-holdout scores are never compared."""

from stocklab_ml.versioning.validation import canonical
from .evaluation import BlockedEvidence


def compare_evaluations(active, candidate, dataset):
    deltas = {}
    if active is not None:
        for field in ("start_date", "end_date", "symbols", "config", "initial_capital"):
            if canonical(active["backtest"][field]) != canonical(candidate["backtest"][field]):
                raise BlockedEvidence("DATASET_MISMATCH")
        for field in ("equity_dates", "benchmark", "benchmark_equity_sha256", "evaluation_keys_sha256"):
            if canonical(active[field]) != canonical(candidate[field]):
                raise BlockedEvidence("DATASET_MISMATCH")
        deltas = {metric + "_delta": candidate["classification"][metric] - active["classification"][metric]
                  for metric in ("accuracy", "precision", "recall", "f1")}
        deltas.update({metric + "_delta": candidate["backtest"][metric] - active["backtest"][metric]
                       for metric in ("total_return", "max_drawdown", "final_value", "win_rate", "executed_trades")})
    for model in (active, candidate):
        if model is not None and model["evaluation_keys_sha256"] != dataset["labels_sha256"]:
            raise BlockedEvidence("DATASET_MISMATCH")
    return {"dataset": dataset, "active": active, "candidate": candidate,
            "benchmark": candidate["benchmark"], "metric_deltas": deltas}
