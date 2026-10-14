"""Shared numeric accounting for simulation and persistence validation."""

from fractions import Fraction
import math

from .contracts import BacktestConfig, BacktestError


def _add_buy(old_quantity: float, old_cost: float, quantity: float, price: float,
             *, old_basis: Fraction | None = None) -> tuple[float, float]:
    exact_quantity = Fraction(str(old_quantity)) + Fraction(str(quantity))
    new_quantity = _finite_sum((exact_quantity,))
    if Fraction(str(new_quantity)) != exact_quantity:
        raise BacktestError("Accumulated quantity cannot be preserved at float64 precision.")
    if old_basis is None:
        old_basis = _product(old_quantity, old_cost)
    basis = old_basis + _product(quantity, price)
    return new_quantity, _finite_sum((basis / exact_quantity,))


def _finite_sum(values) -> float:
    try:
        total = float(sum((Fraction(str(value)) for value in values), Fraction(0)))
    except (OverflowError, ValueError):
        raise BacktestError("Portfolio sum exceeds finite float64 precision.") from None
    if not math.isfinite(total):
        raise BacktestError("Portfolio sum exceeds finite float64 precision.")
    return total


def _product(left: float, right: float) -> Fraction:
    return Fraction(str(left)) * Fraction(str(right))


def _pnl(proceeds: Fraction, basis: Fraction) -> float:
    exact = proceeds - basis
    published = _finite_sum((exact,))
    if exact and published == 0:
        raise BacktestError("Nonzero P&L cannot be preserved at float64 precision.")
    return published


def _opening_value(cash: float | Fraction, positions: dict, marks: dict) -> Fraction:
    return Fraction(str(cash)) + sum((Fraction(str(state[0])) * Fraction(str(marks[symbol]))
                                      for symbol, state in positions.items()), Fraction(0))


def _buy_limits(cash: float | Fraction, total: Fraction, quantity: float, price: float,
                config: BacktestConfig) -> tuple[Fraction, Fraction]:
    remaining = total * Fraction(str(config.max_symbol_exposure)) - Fraction(str(quantity)) * Fraction(str(price))
    budget = min(Fraction(str(cash)), total * Fraction(str(config.max_trade_allocation)), remaining)
    return remaining, budget


def _size_buy(budget: float | Fraction, price: float, decimals: int) -> tuple[float, Fraction]:
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
    # Move by representable floats; one grid unit may round to the same float.
    while amount > available or Fraction(str(float(amount))) > available:
        lower = Fraction(str(math.nextafter(quantity, 0.0))) * scale
        units = lower.numerator // lower.denominator
        quantity = float(Fraction(units, scale))
        amount = Fraction(str(quantity)) * opening_price
    return quantity, amount
