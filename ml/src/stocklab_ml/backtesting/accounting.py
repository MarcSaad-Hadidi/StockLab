"""Shared numeric accounting for simulation and persistence validation."""

from decimal import Context, Decimal, localcontext
from fractions import Fraction
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
        total = float(sum((Fraction(str(value)) for value in values), Fraction(0)))
    except (OverflowError, ValueError):
        raise BacktestError("Portfolio sum exceeds finite float64 precision.") from None
    if not math.isfinite(total):
        raise BacktestError("Portfolio sum exceeds finite float64 precision.")
    return total


def _product(left: float, right: float) -> float:
    try:
        return float(Fraction(str(left)) * Fraction(str(right)))
    except (OverflowError, ValueError):
        raise BacktestError("Portfolio product exceeds finite float64 precision.") from None


def _size_buy(budget: float, price: float, decimals: int) -> tuple[float, float]:
    available, opening_price = Fraction(str(budget)), Fraction(str(price))
    scale = 10 ** decimals
    scaled = available / opening_price * scale
    try:
        if not math.isfinite(float(scaled)):
            raise OverflowError
    except OverflowError:
        raise BacktestError("Quantity exceeds finite float64 precision.") from None
    units = scaled.numerator // scaled.denominator
    quantity = float(Fraction(units, scale))
    amount = Fraction(str(quantity)) * opening_price
    # A float64 output may represent a large grid quantity above its budget.
    if amount > available:
        quantity = float(Fraction(max(0, units - 1), scale))
        amount = Fraction(str(quantity)) * opening_price
    if amount > available:
        return 0.0, 0.0
    return quantity, float(amount)
