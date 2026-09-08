import type { HistoryQuery, StockHistory } from "../api/marketDataApi.ts";
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
  price: number;
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
    interval: range === "MAX" ? "Month" : range === "5Y" ? "Week" : "Day",
  };
}
export function historyPoints(history: StockHistory, locale: string) {
  return history.bars.map((bar) => ({
    value: bar.close,
    label:
      bar.periodDate ??
      new Intl.DateTimeFormat(locale, {
        dateStyle: "short",
        timeStyle: "short",
        timeZone: "UTC",
      }).format(new Date(bar.openTimeUtc!)) + " UTC",
  }));
}
export function money(
  value: number | null | undefined,
  currency: string | null,
  locale: string,
) {
  if (value == null || !Number.isFinite(value)) return "—";
  return new Intl.NumberFormat(locale, {
    ...(currency && /^[A-Z]{3}$/.test(currency)
      ? { style: "currency", currency }
      : {}),
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}
export function calculateTradeTotal(price: number, quantity: number) {
  if (
    !Number.isFinite(price) ||
    !Number.isFinite(quantity) ||
    price <= 0 ||
    quantity <= 0
  )
    return 0;
  return Math.round(price * quantity * 100) / 100;
}
export function getTradeExecutionPrice(
  orderType: TradeOrderType,
  marketPrice: number,
  limitPrice?: number,
) {
  if (!Number.isFinite(marketPrice) || marketPrice <= 0) return 0;
  if (
    orderType === "limit" &&
    typeof limitPrice === "number" &&
    Number.isFinite(limitPrice) &&
    limitPrice > 0
  )
    return limitPrice;
  if (orderType === "limit") return 0;
  return marketPrice;
}
