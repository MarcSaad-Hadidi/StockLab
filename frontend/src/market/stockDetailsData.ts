import type { HistoryQuery, StockHistory } from "../api/marketDataApi.ts";
import { isCalculatedAmount, isPositiveAmount } from '../api/decimalAmount';
import { normalizeOperand, tradeProduct } from '../api/decimalMath';
export const chartRanges = [
  "1D",
  "5D",
  "1M",
  "3M",
  "6M",
  "YTD",
  "1Y",
  "5Y",
  "MAX",
] as const;
export type ChartRange = (typeof chartRanges)[number];
export type TradeOrderType = "market" | "limit";
export type StockDetails = {
  symbol: string;
  company: string;
  price: string;
  currency: string | null;
};
export function historyQuery(
  range: ChartRange,
  now: Date,
): HistoryQuery | null {
  const to = new Date(now);
  const from = new Date(now);
  if (range === "1D" || range === "5D") {
    from.setUTCDate(from.getUTCDate() - (range === "1D" ? 1 : 5));
    return {
      from: from.toISOString(),
      to: to.toISOString(),
      interval: range === "1D" ? "Minute" : "Hour",
    };
  }
  // Calendar boundaries are date strings, never synthetic bar timestamps.
  to.setUTCDate(to.getUTCDate() + 1);
  if (range === "MAX") {
    from.setTime(to.getTime());
    from.setUTCDate(from.getUTCDate() - 4999);
  } else if (range === "YTD") from.setUTCMonth(0, 1);
  else {
    const months = { "1M": 1, "3M": 3, "6M": 6, "1Y": 12, "5Y": 60 };
    const day = from.getUTCDate();
    from.setUTCDate(1);
    from.setUTCMonth(from.getUTCMonth() - months[range]);
    const last = new Date(
      Date.UTC(from.getUTCFullYear(), from.getUTCMonth() + 1, 0),
    ).getUTCDate();
    from.setUTCDate(Math.min(day, last));
  }
  return {
    from: from.toISOString().slice(0, 10),
    to: to.toISOString().slice(0, 10),
    // Daily bars keep the bounded MAX window valid across stock splits: upstream
    // unadjusted monthly OHLC can mix pre-split opens with post-split highs/lows.
    interval: range === "5Y" ? "Week" : "Day",
  };
}
// Sample labels, not observations: all received closes stay on the plotted curve.
export function stockChartSeries(history: StockHistory, range: ChartRange, locale: string) {
  const intraday = history.interval === "Minute" || history.interval === "Hour";
  const style: Intl.DateTimeFormatOptions = { timeZone: "UTC" };
  const pointFormat = new Intl.DateTimeFormat(locale, {
    ...style, dateStyle: "medium", ...(intraday ? { timeStyle: "short" } : {}),
  });
  const tickFormat = new Intl.DateTimeFormat(locale, {
    ...style,
    ...(range === "1D" ? { hour: "numeric", minute: "2-digit" }
      : range === "5Y" || range === "MAX" ? { month: "short", year: "numeric" }
        : { month: "short", day: "numeric" }),
  });
  const maxLabels = range === "1D" ? 5 : range === "5D" ? 6 : range === "5Y" || range === "MAX" ? 7 : 6;
  const values: number[] = [];
  const labels: string[] = [];
  const dates: number[] = [];
  let min = Infinity;
  let max = -Infinity;
  for (const bar of history.bars) {
    const date = Date.parse(bar.periodDate ?? bar.openTimeUtc!);
    dates.push(date);
    values.push(bar.close);
    labels.push(pointFormat.format(date) + (intraday ? " UTC" : ""));
    min = Math.min(min, bar.close);
    max = Math.max(max, bar.close);
  }
  const tickIndexes: number[] = [];
  const ticks = new Map<number, string>();
  const count = Math.min(dates.length, maxLabels);
  for (let i = 0; i < count; i++) {
    const index = count === 1 ? 0 : Math.round(i * (dates.length - 1) / (count - 1));
    const label = tickFormat.format(dates[index]);
    if (tickIndexes.length && ticks.get(tickIndexes.at(-1)!) === label) continue;
    tickIndexes.push(index);
    ticks.set(index, label);
  }
  return { values, labels, tickIndexes, ticks, min: min * 0.99, max: max * 1.01 };
}
export function money(
  value: number | string | null | undefined,
  currency: string | null,
  locale: string,
  maximumFractionDigits = 2,
) {
  if (value == null || (typeof value === 'string' ? !isCalculatedAmount(value) : !Number.isFinite(value))) return "—";
  return new Intl.NumberFormat(locale, {
    ...(currency && /^[A-Z]{3}$/.test(currency)
      ? { style: "currency", currency }
      : {}),
    minimumFractionDigits: 2,
    maximumFractionDigits,
  }).format(value as number);
}
export function calculateTradeTotal(price: string, quantity: string) {
  const normalizedPrice = normalizeOperand(price, 4);
  const normalizedQuantity = normalizeOperand(quantity, 8);
  if (!isPositiveAmount(normalizedPrice) || !isPositiveAmount(normalizedQuantity)) return '0';
  return tradeProduct(normalizedPrice, normalizedQuantity);
}
export function getTradeExecutionPrice(
  orderType: TradeOrderType,
  marketPrice: string,
  limitPrice?: string,
) {
  const normalizedMarketPrice = normalizeOperand(marketPrice, 4);
  if (!isPositiveAmount(normalizedMarketPrice)) return '0';
  if (orderType === 'limit') return normalizeOperand(limitPrice ?? '', 4) ?? '0';
  return normalizedMarketPrice;
}
