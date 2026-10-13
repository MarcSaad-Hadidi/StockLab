"""Shared numeric accounting for simulation and persistence validation."""

from decimal import Context, Decimal, localcontext
import math

from .contracts import BacktestError


def _add_buy(old_quantity: float, old_cost: float, quantity: float, price: float) -> tuple[float, float]:
    # Decimal weights avoid binary cancellation creating break-even trade wins.
    with localcontext(Context(prec=40)):
        old, bought = Decimal(str(old_quantity)), Decimal(str(quantity))
        total = old + bought
        average = (old * Decimal(str(old_cost)) + bought * Decimal(str(price))) / total
        return float(total), float(average)


def _finite_sum(values) -> float:
    try:
        total = math.fsum(values)
    except OverflowError:
        raise BacktestError("Portfolio sum exceeds finite float64 precision.") from None
    if not math.isfinite(total):
        raise BacktestError("Portfolio sum exceeds finite float64 precision.")
    return total
