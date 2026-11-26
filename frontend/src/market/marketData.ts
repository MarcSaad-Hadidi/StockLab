import type { MarketAssetCategory, StockQuote, StockSearchResult } from "../api/marketDataApi.ts";

export const assetCategories = ["stocks", "etfs", "indices", "crypto"] as const;
export type MarketAssetFilter = "all" | MarketAssetCategory;

const categoryForType: Record<string, MarketAssetCategory> = {
  "Common Stock": "stocks",
  "Preferred Stock": "stocks",
  "American Depositary Receipt": "stocks",
  "Global Depositary Receipt": "stocks",
  "Depositary Receipt": "stocks",
  REIT: "stocks",
  ETF: "etfs",
  Index: "indices",
  Indices: "indices",
  "Digital Currency": "crypto",
  Cryptocurrency: "crypto",
};

export function matchesAssetFilter(asset: StockSearchResult, filter: MarketAssetFilter) {
  return filter === "all" || (asset.instrumentType != null && categoryForType[asset.instrumentType] === filter);
}

export function canWatchSymbol(symbol: string) {
  return symbol.length <= 32 && !symbol.includes("/");
}

export async function loadMarketCatalog(
  filter: MarketAssetFilter,
  browse: (category: MarketAssetCategory, signal: AbortSignal) => Promise<StockSearchResult[]>,
  signal: AbortSignal,
) {
  const categories = filter === "all" ? assetCategories : [filter];
  const responses = await Promise.allSettled(categories.map(category => browse(category, signal)));
  signal.throwIfAborted();
  const groups = responses.flatMap(response => response.status === "fulfilled" ? [response.value] : []);
  const failure = responses.find(response => response.status === "rejected");
  if (!groups.length && failure) throw failure.reason;

  const assets = Array.from({ length: Math.max(0, ...groups.map(group => group.length)) }, (_, index) =>
    groups.flatMap(group => group[index] ? [group[index]] : []),
  ).flat();
  return { assets, partial: failure !== undefined };
}

/** Maximum of five featured quotes, with three concurrent requests and no polling. */
export async function loadFeaturedQuotes(
  assets: StockSearchResult[],
  load: (symbol: string, signal: AbortSignal) => Promise<StockQuote>,
  signal: AbortSignal,
) {
  const results: { symbol: string; quote?: StockQuote; failed?: boolean }[] =
    assets.slice(0, 5).map(({ symbol }) => ({ symbol }));
  let cursor = 0;
  let firstFailure: unknown;
  await Promise.all(
    Array.from({ length: 3 }, async () => {
      while (cursor < results.length) {
        signal.throwIfAborted();
        const item = results[cursor++];
        try {
          item.quote = await load(item.symbol, signal);
        } catch (error) {
          signal.throwIfAborted();
          item.failed = true;
          firstFailure ??= error;
        }
      }
    }),
  );
  if (results.length && results.every((item) => item.failed)) throw firstFailure;
  return results;
}
