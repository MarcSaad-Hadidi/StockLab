import type { StockQuote } from "../api/marketDataApi.ts";
export const featuredSymbols = [
  "AAPL",
  "MSFT",
  "NVDA",
  "AMZN",
  "GOOGL",
] as const;
/** Only curated identifiers. Maximum three concurrent price requests; no retries or polling. */
export async function loadFeaturedQuotes(
  load: (symbol: string, signal: AbortSignal) => Promise<StockQuote>,
  signal: AbortSignal,
) {
  const results: { symbol: string; quote?: StockQuote; failed?: boolean }[] =
    featuredSymbols.map((symbol) => ({ symbol }));
  let cursor = 0;
  await Promise.all(
    Array.from({ length: 3 }, async () => {
      while (cursor < results.length) {
        signal.throwIfAborted();
        const item = results[cursor++];
        try {
          item.quote = await load(item.symbol, signal);
        } catch {
          signal.throwIfAborted();
          item.failed = true;
        }
      }
    }),
  );
  return results;
}
