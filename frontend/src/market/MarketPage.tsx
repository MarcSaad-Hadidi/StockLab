import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import { marketDataApi } from "../api/marketDataClient";
import type { StockSearchResult } from "../api/marketDataApi";
import { MarketShell } from "./MarketShell";
import { MarketIcon } from "./marketIcons";
import { StockLogo } from "./StockLogo";
import { assetCategories, canWatchSymbol, loadFeaturedQuotes, loadMarketCatalog, matchesAssetFilter, type MarketAssetFilter } from "./marketData";
import { money } from "./stockDetailsData";
import { useMarketRequest } from "./useMarketRequest";
import { MarketRequestStatus } from "./MarketRequestStatus";
import { localeForLanguage, formatSignedPercent } from "../i18n/formatters";
import { useWatchlistData } from "../watchlist/useWatchlistData";
import { WatchlistFeedback } from "../watchlist/WatchlistFeedback";
import { getAuthSession } from "../auth/authStorage";
import { MarketDataProvenance } from '../components/market-data/MarketDataProvenance';

export function MarketPage({
  onOpenStock,
}: {
  onOpenStock: (symbol: string, instrumentType?: string | null) => void;
}) {
  const { t, i18n } = useTranslation();
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [assetFilter, setAssetFilter] = useState<MarketAssetFilter>("all");
  const assetTerm = query.trim();
  const isSearching = assetTerm.length > 0;
  const loadCatalog = useCallback(
    (signal: AbortSignal) => loadMarketCatalog(assetFilter, marketDataApi.browse, signal),
    [assetFilter],
  );
  const catalog = useMarketRequest(assetFilter, loadCatalog);
  const loadPopular = useCallback(
    (signal: AbortSignal) => loadFeaturedQuotes(catalog.data?.assets ?? [], marketDataApi.quote, signal),
    [catalog.data],
  );
  const popular = useMarketRequest(assetFilter, loadPopular, Boolean(catalog.data?.assets.length));
  const loadMovers = useCallback(
    (signal: AbortSignal) => marketDataApi.movers(signal),
    [],
  );
  const movers = useMarketRequest("movers", loadMovers);
  const watchlist = useWatchlistData(false);
  const { favorites } = watchlist;
  const [watchlistNotice, setWatchlistNotice] = useState<{ symbol: string; key: string; sessionToken: string } | null>(null);
  async function toggleFavorite(symbol: string) {
    setWatchlistNotice(null);
    const removing = favorites.has(symbol);
    const sessionToken = getAuthSession()?.accessToken;
    const success = await (removing ? watchlist.remove(symbol) : watchlist.add(symbol));
    if (success && sessionToken) setWatchlistNotice({ symbol, key: removing ? 'watchlist.removedToast' : 'watchlist.addedToast', sessionToken });
  }
  const load = useCallback(
    (signal: AbortSignal) => marketDataApi.search(assetTerm, signal),
    [assetTerm],
  );
  const search = useMarketRequest(assetTerm, load, isSearching, 450);
  const loadIndices = useCallback(
    (signal: AbortSignal) => marketDataApi.browse("indices", signal, assetTerm),
    [assetTerm],
  );
  const indexSearch = useMarketRequest(assetTerm, loadIndices,
    isSearching && (assetFilter === "all" || assetFilter === "indices"), 450);
  const matches = [...(search.data ?? []), ...(indexSearch.data ?? [])]
    .filter(asset => matchesAssetFilter(asset, assetFilter));
  const results = isSearching
    ? [...new Map(matches.map(asset => [asset.symbol, asset])).values()]
    : catalog.data?.assets ?? [];
  const openAsset = (asset: StockSearchResult) => onOpenStock(asset.symbol, asset.instrumentType);
  const rankedMovers = (positive: boolean) =>
    (positive ? movers.data?.gainers ?? [] : movers.data?.losers ?? [])
      .filter((stock) => positive ? stock.changePercent > 0 : stock.changePercent < 0)
      .sort((a, b) => positive ? b.changePercent - a.changePercent : a.changePercent - b.changePercent);
  const pages = Math.max(1, Math.ceil(results.length / 10));
  const visiblePage = Math.min(page, pages);
  return (
    <MarketShell>
      <section aria-labelledby="market-title" className="market-intro">
        <h1 id="market-title">{t("market.title")}</h1>
        <p>{t("marketApi.subtitle")}</p>
      </section>
      <WatchlistFeedback loading={watchlist.loading} error={watchlist.error} retry={watchlist.reload} />
      <WatchlistFeedback error={watchlist.mutationError} />
      {watchlistNotice && !watchlist.loading && !watchlist.error && watchlistNotice.sessionToken === getAuthSession()?.accessToken && <div className="watchlist-feedback" role="status">{t(watchlistNotice.key, { symbol: watchlistNotice.symbol })}</div>}
      <label className="market-search-bar">
        <MarketIcon name="search" size={17} />
        <span className="market-sr-only">{t("market.searchLabel")}</span>
        <input
          aria-label={t("market.searchLabel")}
          placeholder={t("market.searchPlaceholder")}
          type="search"
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setPage(1);
          }}
        />
      </label>
      <div className="market-filter-row">
        <div className="market-filter-tabs" role="group" aria-label={t("market.filtersLabel")}>
          {(["all", ...assetCategories] as const).map((filter) => (
            <button
              key={filter}
              aria-pressed={assetFilter === filter}
              onClick={() => { setAssetFilter(filter); setPage(1); }}
              className={`market-filter-tab ${filter === assetFilter ? "market-filter-tab-active" : ""}`}
              type="button"
            >
              {t(`market.filters.${filter}`)}
            </button>
          ))}
        </div>
      </div>
      <div className="market-overview-grid">
        <section className="market-overview-card market-overview-card-popular">
          <div className="market-overview-heading">
            <h2>
              {t("market.featured", { asset: t(`market.filterNouns.${assetFilter}`) })}
            </h2>
          </div>
          <div className="market-overview-list">
            <MarketRequestStatus {...catalog} />
            {catalog.data?.partial && <MarketRequestStatus loading={false} error="marketApi.partialCatalog" retry={catalog.retry} />}
            <MarketRequestStatus {...popular} />
            {popular.data?.some((item) => item.failed) && (
              <MarketRequestStatus loading={false} error="marketApi.partialQuotes" retry={popular.retry} />
            )}
            {catalog.data?.assets.slice(0, 5).map((asset) => {
              const item = popular.data?.find(row => row.symbol === asset.symbol);
              return (
                <button
                  className="market-overview-row"
                  key={asset.symbol}
                  type="button"
                  onClick={() => openAsset(asset)}
                >
                  <StockLogo symbol={asset.symbol} instrumentType={asset.instrumentType} />
                  <span className="market-stock-copy">
                    <strong>{asset.symbol}</strong>
                    <small>{item?.failed ? t("marketApi.quoteUnavailable") : asset.companyName}</small>
                  </span>
                  <div className="market-stock-quote">
                    <strong>
                      {money(
                        item?.quote?.price,
                        item?.quote?.currency ?? null,
                        localeForLanguage(i18n.language),
                      )}
                    </strong>
                    <span
                      className={
                        (item?.quote?.changePercent ?? 0) >= 0
                          ? "market-stock-change market-positive"
                          : "market-stock-change market-negative"
                      }
                    >
                      {item?.quote?.changePercent == null
                        ? "—"
                        : formatSignedPercent(item.quote.changePercent)}
                    </span>
                  </div>
                  {item?.quote && <MarketDataProvenance {...item.quote} compact />}
                </button>
              );
            })}
          </div>
        </section>
        {["topGainers", "topLosers"].map((section) => (
          <section
            key={section}
            className={`market-overview-card market-overview-card-${section === "topGainers" ? "positive" : "negative"}`}
          >
            <div className="market-overview-heading">
              <h2>
                {t(`market.${section}`)}
              </h2>
            </div>
            <div className="market-movers-description">
            {movers.data && <MarketDataProvenance source={movers.data.source} sourceOnly />}
            <small>
              {t("marketApi.eodMovers")}
              {movers.data?.lastUpdated && ` · ${movers.data.lastUpdated}`}
            </small>
            </div>
            <MarketRequestStatus {...movers} label="marketApi.marketRanking" />
            {movers.data && rankedMovers(section === "topGainers").length === 0 && (
              <p className="market-card-empty">{t("marketApi.noMovers")}</p>
            )}
            <div className="market-overview-list">
              {rankedMovers(section === "topGainers")
                .slice(0, 5)
                .map((stock) => (
                  <button
                    className="market-overview-row"
                    key={stock.symbol}
                    type="button"
                    onClick={() => onOpenStock(stock.symbol)}
                  >
                    <StockLogo symbol={stock.symbol} />
                    <span className="market-stock-copy">
                      <strong>{stock.symbol}</strong>
                      <small>{stock.symbol}</small>
                    </span>
                    <div className="market-stock-quote">
                      <strong>
                        {money(
                          stock.price,
                          "USD",
                          localeForLanguage(i18n.language),
                        )}
                      </strong>
                      <span
                        className={
                          stock.changePercent >= 0
                            ? "market-stock-change market-positive"
                            : "market-stock-change market-negative"
                        }
                      >
                        {formatSignedPercent(stock.changePercent)}
                      </span>
                    </div>
                  </button>
                ))}
            </div>
          </section>
        ))}
      </div>
      <section
        className="market-results-panel"
        aria-label={t("market.searchLabel")}
      >
        <div className="market-results-heading">
          <h2>{t(isSearching ? "market.searchResults" : "market.availableAssets")}</h2>
          <span>{results.length}</span>
        </div>
        <MarketRequestStatus {...search} />
        <MarketRequestStatus {...indexSearch} label={assetFilter === "all" ? "market.filters.indices" : undefined} />

        {results.length === 0 && (isSearching
          ? !search.loading && !indexSearch.loading && !search.error && !indexSearch.error
          : Boolean(catalog.data)) && (
          <div className="market-empty-state" role="status">
            {t("market.noAssetsFound")}
          </div>
        )}
        {results.length > 0 && (
          <>
            <div className="market-table-scroll">
              <table className="market-results-table">
                <thead>
                  <tr>
                    {["symbol", "assetName"].map((column) => (
                      <th key={column}>{t(`market.columns.${column}`)}</th>
                    ))}
                    <th>{t("marketApi.exchange")}</th>
                    <th>{t("marketApi.currency")}</th>
                    <th>{t("market.columns.price")}</th>
                    <th>{t("market.columns.favorite")}</th>
                  </tr>
                </thead>
                <tbody>
                  {results.slice((visiblePage - 1) * 10, visiblePage * 10).map((stock) => {
                    const quote = popular.data?.find(item => item.symbol === stock.symbol)?.quote;
                    const watchable = canWatchSymbol(stock.symbol);
                    return (
                      <tr key={stock.symbol}>
                        <td>
                          <button
                            className="market-symbol-cell"
                            type="button"
                            onClick={() => openAsset(stock)}
                          >
                            <StockLogo symbol={stock.symbol} instrumentType={stock.instrumentType} />
                            <strong>{stock.symbol}</strong>
                          </button>
                        </td>
                        <td>
                          <button
                            className="market-company-cell"
                            type="button"
                            onClick={() => openAsset(stock)}
                          >
                            <span>{stock.companyName}</span>
                            {stock.instrumentType && <small className="market-result-type">{stock.instrumentType}</small>}
                          </button>
                        </td>
                        <td>{stock.exchange ?? "—"}</td>
                        <td>{stock.currency ?? "—"}</td>
                        <td title={t("marketApi.priceOnDetails")}>
                          {money(
                            quote?.price,
                            stock.currency,
                            localeForLanguage(i18n.language),
                          )}
                          {quote && <MarketDataProvenance {...quote} compact />}
                        </td>
                        <td>
                          <button
                            className="market-favorite-button"
                            type="button"
                            aria-label={t(
                              favorites.has(stock.symbol)
                                ? "market.removeFavorite"
                                : "market.addFavorite",
                              { symbol: stock.symbol },
                            )}
                            aria-pressed={favorites.has(stock.symbol)}
                            disabled={!watchable || watchlist.loading || Boolean(watchlist.error) || watchlist.pendingSymbols.has(stock.symbol)}
                            title={watchable ? undefined : t("marketApi.watchlistUnavailable")}
                            onClick={() => { void toggleFavorite(stock.symbol); }}
                          >
                            <MarketIcon
                              name="star"
                              filled={favorites.has(stock.symbol)}
                              size={14}
                            />
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            <div className="market-pagination">
              <button
                className="market-page-arrow"
                type="button"
                disabled={visiblePage === 1}
                onClick={() => setPage(visiblePage - 1)}
              >
                {t("common.previousPage")}
              </button>
              <span className="market-page-count">
                {visiblePage} / {pages}
              </span>
              <button
                className="market-page-arrow"
                type="button"
                disabled={visiblePage === pages}
                onClick={() => setPage(visiblePage + 1)}
              >
                {t("common.nextPage")}
              </button>
            </div>
          </>
        )}
      </section>
    </MarketShell>
  );
}
