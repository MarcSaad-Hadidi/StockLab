import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import { marketDataApi } from "../api/marketDataClient";
import { MarketShell } from "./MarketShell";
import { MarketIcon } from "./marketIcons";
import { StockLogo } from "./StockLogo";
import { loadFeaturedQuotes } from "./marketData";
import { money } from "./stockDetailsData";
import { useMarketRequest } from "./useMarketRequest";
import { MarketRequestStatus } from "./MarketRequestStatus";
import { localeForLanguage, formatSignedPercent } from "../i18n/formatters";

export function MarketPage({
  onOpenStock,
}: {
  onOpenStock: (symbol: string) => void;
}) {
  const { t, i18n } = useTranslation();
  const loadPopular = useCallback(
    (signal: AbortSignal) => loadFeaturedQuotes(marketDataApi.quote, signal),
    [],
  );
  const popular = useMarketRequest("popular", loadPopular);
  const loadMovers = useCallback(
    (signal: AbortSignal) => marketDataApi.movers(signal),
    [],
  );
  const [showMovers, setShowMovers] = useState(false);
  const movers = useMarketRequest("movers", loadMovers, showMovers);
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [assetFilter, setAssetFilter] = useState("all");
  const favorites = new Set<string>();
  const load = useCallback(
    (signal: AbortSignal) => marketDataApi.search(query, signal),
    [query],
  );
  const search = useMarketRequest(query, load, query.trim().length > 0, 450);
  const results = query.trim()
    ? (search.data ?? [])
    : (popular.data ?? []).map((item) => ({
        symbol: item.symbol,
        companyName: item.quote?.name ?? item.symbol,
        exchange: item.quote?.exchange ?? null,
        currency: item.quote?.currency ?? null,
      }));
  const featuredMovers = (positive: boolean) =>
    (popular.data ?? [])
      .flatMap((item) =>
        item.quote?.changePercent != null &&
        (positive ? item.quote.changePercent > 0 : item.quote.changePercent < 0)
          ? [
              {
                symbol: item.symbol,
                price: item.quote.price,
                changePercent: item.quote.changePercent,
              },
            ]
          : [],
      )
      .sort((a, b) =>
        positive
          ? b.changePercent - a.changePercent
          : a.changePercent - b.changePercent,
      );
  const pages = Math.max(1, Math.ceil(results.length / 10));
  return (
    <MarketShell>
      <section aria-labelledby="market-title" className="market-intro">
        <h1 id="market-title">{t("market.title")}</h1>
        <p>{t("marketApi.subtitle")}</p>
      </section>
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
        <div className="market-filter-tabs">
          {["all", "stocks", "etfs", "indices", "crypto"].map((filter) => (
            <button
              key={filter}
              disabled={!["all", "stocks"].includes(filter)}
              aria-pressed={assetFilter === filter}
              onClick={() => setAssetFilter(filter)}
              className={`market-filter-tab ${filter === assetFilter ? "market-filter-tab-active" : ""}`}
              title={t("marketApi.commonStocks")}
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
              {t("market.popular", { asset: t("market.filterNouns.stocks") })}
            </h2>
          </div>
          <div className="market-overview-list">
            <MarketRequestStatus {...popular} />
            {(popular.data ?? []).map((item) => (
              <button
                className="market-overview-row"
                key={item.symbol}
                type="button"
                onClick={() => onOpenStock(item.symbol)}
              >
                <StockLogo symbol={item.symbol} />
                <span className="market-stock-copy">
                  <strong>{item.symbol}</strong>
                  <small>{item.quote?.name ?? item.symbol}</small>
                </span>
                <div className="market-stock-quote">
                  <strong>
                    {money(
                      item.quote?.price,
                      item.quote?.currency ?? null,
                      localeForLanguage(i18n.language),
                    )}
                  </strong>
                  <span
                    className={
                      (item.quote?.changePercent ?? 0) >= 0
                        ? "market-positive"
                        : "market-negative"
                    }
                  >
                    {item.quote?.changePercent == null
                      ? "—"
                      : formatSignedPercent(item.quote.changePercent)}
                  </span>
                </div>
              </button>
            ))}
          </div>
        </section>
        {["topGainers", "topLosers"].map((section) => (
          <section
            key={section}
            className={`market-overview-card market-overview-card-${section === "topGainers" ? "positive" : "negative"}`}
          >
            <div className="market-overview-heading">
              <h2>
                {movers.data
                  ? t(`market.${section}`)
                  : t(
                      section === "topGainers"
                        ? "marketApi.featuredGainers"
                        : "marketApi.featuredLosers",
                    )}
              </h2>
            </div>
            {!showMovers && (
              <button
                className="market-view-all"
                onClick={() => setShowMovers(true)}
                type="button"
              >
                {t("marketApi.loadMovers")}
              </button>
            )}
            <MarketRequestStatus {...movers} />
            <small>
              {t(
                movers.data ? "marketApi.eodMovers" : "marketApi.featuredScope",
              )}
              {movers.data?.lastUpdated && ` · ${movers.data.lastUpdated}`}
            </small>
            <div className="market-overview-list">
              {(
                (section === "topGainers"
                  ? movers.data?.gainers
                  : movers.data?.losers) ??
                featuredMovers(section === "topGainers")
              )
                .slice(0, 5)
                .map((stock) => (
                  <button
                    className="market-overview-row"
                    key={stock.symbol}
                    type="button"
                    onClick={() => onOpenStock(stock.symbol)}
                  >
                    <StockLogo symbol={stock.symbol} load={false} />
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
                            ? "market-positive"
                            : "market-negative"
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
          <h2>{t("market.searchResults")}</h2>
          <span>{results.length}</span>
        </div>
        <MarketRequestStatus {...search} />

        {search.data?.length === 0 && (
          <div className="market-empty-state" role="status">
            {t("market.noStocksFound")}
          </div>
        )}
        {results.length > 0 && (
          <>
            <div className="market-table-scroll">
              <table className="market-results-table">
                <thead>
                  <tr>
                    {["symbol", "company"].map((column) => (
                      <th key={column}>{t(`market.columns.${column}`)}</th>
                    ))}
                    <th>{t("marketApi.exchange")}</th>
                    <th>{t("marketApi.currency")}</th>
                    <th>{t("market.columns.price")}</th>
                    <th>{t("market.columns.favorite")}</th>
                  </tr>
                </thead>
                <tbody>
                  {results.slice((page - 1) * 10, page * 10).map((stock) => (
                    <tr key={stock.symbol}>
                      <td>
                        <button
                          className="market-symbol-cell"
                          type="button"
                          onClick={() => onOpenStock(stock.symbol)}
                        >
                          <StockLogo symbol={stock.symbol} load={false} />
                          <strong>{stock.symbol}</strong>
                        </button>
                      </td>
                      <td>
                        <button
                          className="market-company-cell"
                          type="button"
                          onClick={() => onOpenStock(stock.symbol)}
                        >
                          {stock.companyName}
                        </button>
                      </td>
                      <td>{stock.exchange ?? "—"}</td>
                      <td>{stock.currency ?? "—"}</td>
                      <td title={t("marketApi.priceOnDetails")}>
                        {money(
                          popular.data?.find(
                            (item) => item.symbol === stock.symbol,
                          )?.quote?.price,
                          stock.currency,
                          localeForLanguage(i18n.language),
                        )}
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
                          disabled
                          title={t("businessData.unavailable")}
                        >
                          <MarketIcon
                            name="star"
                            filled={favorites.has(stock.symbol)}
                            size={14}
                          />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="market-pagination">
              <button
                type="button"
                disabled={page === 1}
                onClick={() => setPage((n) => n - 1)}
              >
                {t("common.previousPage")}
              </button>
              <span>
                {page} / {pages}
              </span>
              <button
                type="button"
                disabled={page === pages}
                onClick={() => setPage((n) => n + 1)}
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
