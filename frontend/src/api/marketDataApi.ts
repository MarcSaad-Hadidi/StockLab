export type CompanyLogo = {
  symbol: string;
  pngUrl: string | null;
  svgUrl: string | null;
};
export type StockEarnings = { symbol: string; nextEarningsDate: string | null };
export type StockFundamentals = {
  symbol: string;
  name?: string | null;
  exchange?: string | null;
  currency?: string | null;
  fiftyTwoWeekLow?: number | null;
  fiftyTwoWeekHigh?: number | null;
  marketCap: number | null;
  peRatio: number | null;
  epsTtm: number | null;
  dividendYield: number | null;
  beta: number | null;
  analystTargetPrice: number | null;
  analystRatings: {
    strongBuy: number | null;
    buy: number | null;
    hold: number | null;
    sell: number | null;
    strongSell: number | null;
  } | null;
};
export type MarketMovers = {
  lastUpdated: string | null;
  gainers: MarketMover[];
  losers: MarketMover[];
  mostActive: MarketMover[];
};
export type MarketMover = {
  symbol: string;
  price: number;
  change: number;
  changePercent: number;
  volume: number;
};
export type StockQuote = {
  symbol: string;
  price: number;
  change: number | null;
  changePercent: number | null;
  volume: number | null;
  currency: string;
  asOfUtc: string;
  name: string | null;
  exchange: string | null;
  open: number | null;
  high: number | null;
  low: number | null;
  previousClose: number | null;
  averageVolume: number | null;
  isMarketOpen: boolean | null;
  fiftyTwoWeek: {
    low: number | null;
    high: number | null;
    range: string | null;
  } | null;
};
export type StockSearchResult = {
  symbol: string;
  companyName: string;
  exchange: string | null;
  currency: string | null;
};
export type HistoryInterval = "Minute" | "Hour" | "Day" | "Week" | "Month";
export type HistoryBar = {
  openTimeUtc: string | null;
  periodDate: string | null;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number | null;
};
export type StockHistory = {
  symbol: string;
  currency: string;
  interval: HistoryInterval;
  bars: HistoryBar[];
};
export type HistoryQuery = {
  from: string;
  to: string;
  interval: HistoryInterval;
};
export class MarketDataError extends Error {
  readonly status: number;
  constructor(status: number) {
    super("Market data request failed");
    this.status = status;
  }
}
export function errorKey(error: unknown) {
  const status = error instanceof MarketDataError ? error.status : 0;
  return `marketApi.${({ 400: "invalid", 404: "notFound", 429: "rateLimited", 502: "invalidResponse", 503: "unavailable", 504: "timeout", 500: "serverError" } as Record<number, string>)[status] ?? "offline"}`;
}
const record = (v: unknown): v is Record<string, unknown> =>
  typeof v === "object" && v !== null;
const number = (v: unknown): v is number =>
  typeof v === "number" && Number.isFinite(v);
const nullableNumber = (v: unknown) => v === null || number(v);
const nullableString = (v: unknown) => v === null || typeof v === "string";
const volume = (v: unknown) =>
  v === null || (number(v) && Number.isSafeInteger(v) && v >= 0);
function quote(v: unknown): v is StockQuote {
  return (
    record(v) &&
    typeof v.symbol === "string" &&
    number(v.price) &&
    v.price > 0 &&
    nullableNumber(v.change) &&
    nullableNumber(v.changePercent) &&
    volume(v.volume) &&
    typeof v.currency === "string" &&
    typeof v.asOfUtc === "string" &&
    Number.isFinite(Date.parse(v.asOfUtc)) &&
    nullableString(v.name) &&
    nullableString(v.exchange) &&
    [v.open, v.high, v.low, v.previousClose].every(nullableNumber) &&
    volume(v.averageVolume) &&
    (v.isMarketOpen === null || typeof v.isMarketOpen === "boolean") &&
    (v.fiftyTwoWeek === null ||
      (record(v.fiftyTwoWeek) &&
        nullableNumber(v.fiftyTwoWeek.low) &&
        nullableNumber(v.fiftyTwoWeek.high) &&
        nullableString(v.fiftyTwoWeek.range)))
  );
}
function search(v: unknown): v is StockSearchResult[] {
  return (
    Array.isArray(v) &&
    v.every(
      (s) =>
        record(s) &&
        typeof s.symbol === "string" &&
        typeof s.companyName === "string" &&
        nullableString(s.exchange) &&
        nullableString(s.currency),
    )
  );
}
function history(v: unknown): v is StockHistory {
  if (
    !record(v) ||
    typeof v.symbol !== "string" ||
    typeof v.currency !== "string" ||
    !["Minute", "Hour", "Day", "Week", "Month"].includes(String(v.interval)) ||
    !Array.isArray(v.bars)
  )
    return false;
  const intraday = v.interval === "Minute" || v.interval === "Hour";
  return v.bars.every(
    (b) =>
      record(b) &&
      [b.open, b.high, b.low, b.close].every((n) => number(n) && n > 0) &&
      number(b.high) &&
      number(b.low) &&
      number(b.open) &&
      number(b.close) &&
      b.high >= Math.max(b.open, b.close, b.low) &&
      b.low <= Math.min(b.open, b.close) &&
      volume(b.volume) &&
      (intraday
        ? b.periodDate === null &&
          typeof b.openTimeUtc === "string" &&
          /(?:Z|\+00:00)$/.test(b.openTimeUtc) &&
          Number.isFinite(Date.parse(b.openTimeUtc))
        : b.openTimeUtc === null &&
          typeof b.periodDate === "string" &&
          /^\d{4}-\d{2}-\d{2}$/.test(b.periodDate)),
  );
}
/** Memory only, bounded and expiring. Errors and aborted responses are never cached. */
export function createMarketDataApi(
  baseUrl: string,
  fetcher: typeof fetch = fetch,
) {
  const cache = new Map<string, { value: unknown; expires: number }>();
  const metadata = new Map<string, StockSearchResult>();
  const logoListeners = new Set<() => void>();
  async function get<T>(
    path: string,
    valid: (v: unknown) => v is T,
    signal: AbortSignal,
    ttl: number,
  ): Promise<T> {
    signal.throwIfAborted();
    const cached = cache.get(path);
    if (cached && cached.expires > Date.now()) return cached.value as T;
    let response: Response;
    try {
      response = await fetcher(`${baseUrl.replace(/\/$/, "")}${path}`, {
        signal,
        headers: { Accept: "application/json" },
      });
    } catch {
      signal.throwIfAborted();
      throw new MarketDataError(0);
    }
    if (!response.ok) throw new MarketDataError(response.status);
    let value: unknown;
    try {
      value = await response.json();
    } catch {
      signal.throwIfAborted();
      throw new MarketDataError(502);
    }
    signal.throwIfAborted();
    if (!valid(value)) throw new MarketDataError(502);
    if (cache.size >= 40) cache.delete(cache.keys().next().value!);
    cache.set(path, { value, expires: Date.now() + ttl });
    if (path.endsWith("/logo")) logoListeners.forEach((listener) => listener());
    return value;
  }
  return {
    subscribeLogos(listener: () => void) {
      logoListeners.add(listener);
      return () => {
        logoListeners.delete(listener);
      };
    },
    cachedLogo(symbol: string): CompanyLogo | undefined {
      const entry = cache.get(
        `/api/stocks/${encodeURIComponent(symbol.trim().toUpperCase())}/logo`,
      );
      return entry && entry.expires > Date.now()
        ? (entry.value as CompanyLogo)
        : undefined;
    },
    fundamentals(symbol: string, signal: AbortSignal) {
      return get(
        `/api/stocks/${encodeURIComponent(symbol.trim().toUpperCase())}/fundamentals`,
        (v: unknown): v is StockFundamentals =>
          record(v) &&
          typeof v.symbol === "string" &&
          ["name", "exchange", "currency"].every(key => v[key] === undefined || nullableString(v[key])) &&
          ["fiftyTwoWeekLow", "fiftyTwoWeekHigh"].every(key => v[key] === undefined || nullableNumber(v[key])) &&
          [
            "marketCap",
            "peRatio",
            "epsTtm",
            "dividendYield",
            "beta",
            "analystTargetPrice",
          ].every((k) => nullableNumber(v[k])) &&
          (v.analystRatings === null ||
            (record(v.analystRatings) &&
              Object.values(v.analystRatings).every(
                (count) =>
                  count === null ||
                  (typeof count === "number" &&
                    Number.isSafeInteger(count) &&
                    count >= 0),
              ))),
        signal,
        86_400_000,
      );
    },
    earnings(symbol: string, signal: AbortSignal) {
      return get(
        `/api/stocks/${encodeURIComponent(symbol.trim().toUpperCase())}/earnings`,
        (v: unknown): v is StockEarnings =>
          record(v) &&
          typeof v.symbol === "string" &&
          nullableString(v.nextEarningsDate),
        signal,
        86_400_000,
      );
    },
    logo(symbol: string, signal: AbortSignal) {
      return get(
        `/api/stocks/${encodeURIComponent(symbol.trim().toUpperCase())}/logo`,
        (v: unknown): v is CompanyLogo =>
          record(v) &&
          typeof v.symbol === "string" &&
          [v.pngUrl, v.svgUrl].every(
            (u) =>
              u === null || (typeof u === "string" && /^https:\/\//.test(u)),
          ),
        signal,
        2_592_000_000,
      );
    },
    movers(signal: AbortSignal) {
      return get(
        "/api/market/movers",
        (v: unknown): v is MarketMovers =>
          record(v) &&
          nullableString(v.lastUpdated) &&
          [v.gainers, v.losers, v.mostActive].every(
            (rows) =>
              Array.isArray(rows) &&
              rows.every(
                (r) =>
                  record(r) &&
                  typeof r.symbol === "string" &&
                  number(r.price) && r.price > 0 &&
                  number(r.change) &&
                  number(r.changePercent) &&
                  number(r.volume) && Number.isSafeInteger(r.volume) && r.volume >= 0,
              ),
          ),
        signal,
        43_200_000,
      );
    },
    quote(symbol: string, signal: AbortSignal) {
      return get(
        `/api/stocks/${encodeURIComponent(symbol.trim().toUpperCase())}/quote`,
        quote,
        signal,
        15_000,
      );
    },
    async search(query: string, signal: AbortSignal) {
      signal.throwIfAborted();
      if (!query.trim()) return [];
      const results = await get(
        `/api/stocks/search?${new URLSearchParams({ query: query.trim() })}`,
        search,
        signal,
        300_000,
      );
      for (const result of results) {
        if (metadata.size >= 100)
          metadata.delete(metadata.keys().next().value!);
        metadata.set(result.symbol.toUpperCase(), result);
      }
      return results;
    },
    metadata(symbol: string) {
      return metadata.get(symbol.trim().toUpperCase());
    },
    history(symbol: string, query: HistoryQuery, signal: AbortSignal) {
      return get(
        `/api/stocks/${encodeURIComponent(symbol.trim().toUpperCase())}/history?${new URLSearchParams(query)}`,
        history,
        signal,
        300_000,
      );
    },
  };
}
