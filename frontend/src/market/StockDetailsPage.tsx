import { FinancialLineChart } from "../components/charts/FinancialLineChart";
import { useCallback, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import {
  formatNumber,
  formatSignedPercent,
  localeForLanguage,
} from "../i18n/formatters";
import { marketDataApi } from "../api/marketDataClient";
import { MarketShell } from "./MarketShell";
import { MarketIcon } from "./marketIcons";
import { StockLogo } from "./StockLogo";
import { MarketRequestStatus } from "./MarketRequestStatus";
import { useMarketRequest } from "./useMarketRequest";
import {
  calculateTradeTotal,
  chartRanges,
  historyPoints,
  historyQuery,
  money,
  getTradeExecutionPrice,
  type ChartRange,
  type StockDetails,
  type TradeOrderType,
} from "./stockDetailsData";
type TradeSide = "BUY" | "SELL";
type ToastState = {
  key: string;
  values?: Record<string, string | number>;
  tabKey?: string;
  side?: TradeSide;
  orderType?: TradeOrderType;
};
const detailTabs = [
  "overview",
  "chart",
  "financials",
  "news",
  "keyMetrics",
  "forecast",
  "aiInsights",
] as const;
function StatCard({
  label,
  value,
  metric = false,
}: {
  label: string;
  value: string;
  metric?: boolean;
}) {
  return (
    <article className={metric ? "stock-metric-item" : "stock-stat-card"}>
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  );
}
function TradeTicket({
  details,
  side,
  quantity,
  quantityError,
  orderType,
  limitPrice,
  limitPriceError,
  onSideChange,
  onQuantityChange,
  onOrderTypeChange,
  onLimitPriceChange,
  onSubmit,
}: {
  details: StockDetails;
  side: TradeSide;
  quantity: string;
  quantityError: string;
  orderType: TradeOrderType;
  limitPrice: string;
  limitPriceError: string;
  onSideChange: (side: TradeSide) => void;
  onQuantityChange: (quantity: string) => void;
  onOrderTypeChange: (orderType: TradeOrderType) => void;
  onLimitPriceChange: (limitPrice: string) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
}) {
  const { t, i18n } = useTranslation();
  const formatCurrency = (value: number) =>
    money(value, details.currency, localeForLanguage(i18n.language));
  const parsedQuantity = Number(quantity);
  const estimatedPrice = getTradeExecutionPrice(
    orderType,
    details.price,
    Number(limitPrice),
  );
  const estimatedTotal = calculateTradeTotal(estimatedPrice, parsedQuantity);
  const isBuy = side === "BUY";

  return (
    <form className="stock-trade-card" onSubmit={onSubmit}>
      <div className="stock-card-heading">
        <div className="stock-card-title">
          <span className="stock-card-icon stock-card-icon-trade">
            <MarketIcon name="wallet" size={16} />
          </span>
          <h2>{t("stockDetails.paperTrading")}</h2>
        </div>
        <span className="stock-practice-pill">
          {t("stockDetails.practiceMode")}
        </span>
      </div>
      <div className="stock-trade-tabs">
        <button
          aria-pressed={isBuy}
          className={isBuy ? "stock-trade-tab-active stock-trade-tab-buy" : ""}
          onClick={() => onSideChange("BUY")}
          type="button"
        >
          {t("common.buy")}
        </button>
        <button
          aria-pressed={!isBuy}
          className={
            !isBuy ? "stock-trade-tab-active stock-trade-tab-sell" : ""
          }
          onClick={() => onSideChange("SELL")}
          type="button"
        >
          {t("common.sell")}
        </button>
      </div>
      <div className="stock-trade-field">
        <label htmlFor="stock-order-type">{t("stockDetails.orderType")}</label>
        <select
          id="stock-order-type"
          onChange={(event) =>
            onOrderTypeChange(event.target.value as TradeOrderType)
          }
          value={orderType}
        >
          <option value="market">{t("stockDetails.marketOrder")}</option>
          <option value="limit">{t("stockDetails.limitOrder")}</option>
        </select>
      </div>
      {orderType === "limit" && (
        <div className="stock-trade-field">
          <label htmlFor="stock-limit-price">
            {t("stockDetails.limitPrice")}
          </label>
          <div className="stock-trade-input">
            <input
              aria-describedby={
                limitPriceError ? "stock-limit-price-error" : undefined
              }
              id="stock-limit-price"
              inputMode="decimal"
              min="0.01"
              onChange={(event) => onLimitPriceChange(event.target.value)}
              step="0.01"
              type="number"
              value={limitPrice}
            />
            <span>{details.currency ?? "—"}</span>
          </div>
        </div>
      )}
      <div className="stock-trade-field">
        <label htmlFor="stock-quantity">{t("common.quantity")}</label>
        <div className="stock-trade-input">
          <input
            id="stock-quantity"
            inputMode="numeric"
            min="1"
            onChange={(event) => onQuantityChange(event.target.value)}
            type="number"
            value={quantity}
          />
          <span>{t("stockDetails.sharesLabel")}</span>
        </div>
      </div>
      {quantityError && (
        <p className="stock-form-error" role="alert">
          {t(quantityError)}
        </p>
      )}
      {limitPriceError && (
        <p
          className="stock-form-error"
          id="stock-limit-price-error"
          role="alert"
        >
          {t(limitPriceError)}
        </p>
      )}
      <div className="stock-trade-summary">
        <div>
          <span>{t("stockDetails.estimatedPriceShort")}</span>
          <strong>
            {estimatedPrice > 0 ? formatCurrency(estimatedPrice) : "—"}
          </strong>
        </div>
        <div>
          <span>{t("stockDetails.estimatedTotalShort")}</span>
          <strong>
            {estimatedPrice > 0 ? formatCurrency(estimatedTotal) : "—"}
          </strong>
        </div>
      </div>
      <button
        className={`stock-trade-submit ${isBuy ? "stock-trade-submit-buy" : "stock-trade-submit-sell"}`}
        type="submit"
        disabled
        title={t("businessData.unavailable")}
      >
        {t(
          isBuy ? "stockDetails.placeBuyOrder" : "stockDetails.placeSellOrder",
        )}
      </button>
      <p role="status">{t("businessData.backendPending")}</p>
      <div className="stock-cash-row">
        <span>{t("stockDetails.availableCashPaper")}</span>
        <strong>{"—"}</strong>
      </div>
    </form>
  );
}

export function StockDetailsPage({
  requestedSymbol,
  onBack,
}: {
  requestedSymbol: string;
  onBack: () => void;
}) {
  const { t, i18n } = useTranslation();
  const locale = localeForLanguage(i18n.language);
  const symbol = requestedSymbol.trim().toUpperCase();
  const [anchor] = useState(() => new Date());
  const isWatchlisted = false;
  const [tradeSide, setTradeSide] = useState<TradeSide>("BUY");
  const [orderType, setOrderType] = useState<TradeOrderType>("market");
  const [limitPrice, setLimitPrice] = useState("");
  const [quantity, setQuantity] = useState("10");
  const [quantityError, setQuantityError] = useState("");
  const [limitPriceError, setLimitPriceError] = useState("");
  const [activeRange, setActiveRange] = useState<ChartRange>("3M");
  const [activeTab, setActiveTab] = useState<string>("overview");
  const [toast, setToast] = useState<ToastState | null>(null);

  const showToast = (message: ToastState) => {
    setToast(message);
    window.setTimeout(() => setToast(null), 2500);
  };

  const loadQuote = useCallback(
    (signal: AbortSignal) => marketDataApi.quote(symbol, signal),
    [symbol],
  );
  const quote = useMarketRequest(`quote:${symbol}`, loadQuote);
  const [showEnrichment, setShowEnrichment] = useState(false);
  const loadFundamentals = useCallback(
    (signal: AbortSignal) => marketDataApi.fundamentals(symbol, signal),
    [symbol],
  );
  const fundamentals = useMarketRequest(
    `fundamentals:${symbol}`,
    loadFundamentals,
    showEnrichment,
  );
  const loadEarnings = useCallback(
    (signal: AbortSignal) => marketDataApi.earnings(symbol, signal),
    [symbol],
  );
  const earnings = useMarketRequest(
    `earnings:${symbol}`,
    loadEarnings,
    showEnrichment,
  );

  // For a closed market, 1D follows the latest observed session rather than an empty weekend.
  const historyAnchorTime =
    activeRange === "1D" && quote.data
      ? Date.parse(quote.data.asOfUtc) + 60_000
      : anchor.getTime();
  const loadHistory = useCallback(
    (signal: AbortSignal) =>
      marketDataApi.history(
        symbol,
        historyQuery(activeRange, new Date(historyAnchorTime))!,
        signal,
      ),
    [symbol, activeRange, historyAnchorTime],
  );
  const history = useMarketRequest(
    `history:${symbol}:${activeRange}`,
    loadHistory,
    activeRange !== "1D" || !!quote.data,
  );
  const metadata = marketDataApi.metadata(symbol);
  const companyName = quote.data?.name ?? metadata?.companyName ?? fundamentals.data?.name ?? symbol;
  const exchange = quote.data?.exchange ?? metadata?.exchange ?? fundamentals.data?.exchange;
  const currency = quote.data?.currency ?? metadata?.currency ?? history.data?.currency ?? fundamentals.data?.currency ?? null;
  const formatPrice = (value: number | null | undefined) =>
    money(value, currency, locale);
  const details: StockDetails | null = quote.data
    ? {
        symbol,
        company: companyName,
        price: quote.data.price,
        currency,
      }
    : null;
  const points = history.data ? historyPoints(history.data, locale) : [];
  const change = quote.data?.changePercent;
  const tone =
    change == null || change === 0
      ? ""
      : change > 0
        ? "stock-positive"
        : "stock-negative";
  const submitTrade = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    showToast({ key: "businessData.unavailable" });
  };

  return (
    <MarketShell
      topbarSearch
      breadcrumb={<strong>{t("stockDetails.title")}</strong>}
    >
      <section
        aria-labelledby="stock-details-title"
        className="stock-details-page"
      >
        <button className="stock-details-back" onClick={onBack} type="button">
          <MarketIcon name="arrowLeft" size={16} />{" "}
          {t("stockDetails.backToMarket")}
        </button>
        <header className="stock-details-hero">
          <div className="stock-details-identity">
            <StockLogo size="large" symbol={symbol} load />
            <div>
              <div className="stock-title-row">
                <h1 id="stock-details-title">{companyName}</h1>
                <button
                  className="stock-title-star"
                  type="button"
                  aria-label={t("stockDetails.addToWatchlist", { symbol })}
                  aria-pressed={isWatchlisted}
                  disabled
                  title={t("businessData.unavailable")}
                >
                  <MarketIcon name="star" filled={isWatchlisted} size={17} />
                </button>
              </div>
              <p className="stock-details-subtitle">
                {symbol} · {exchange ?? "—"}
              </p>
              <MarketRequestStatus {...quote} />
              {quote.data && (
                <>
                  <div className="stock-price-row">
                    <strong>{formatPrice(quote.data.price)}</strong>
                    <span className={tone}>
                      {quote.data.change == null
                        ? "—"
                        : `${quote.data.change > 0 ? "+" : ""}${formatPrice(quote.data.change)}`}{" "}
                      ({change == null ? "—" : formatSignedPercent(change)})
                    </span>
                  </div>
                  <p className="stock-details-status">
                    {quote.data.isMarketOpen == null
                      ? t("marketApi.latestQuote")
                      : t(
                          quote.data.isMarketOpen
                            ? "marketApi.marketOpen"
                            : "marketApi.marketClosed",
                        )}{" "}
                    · {new Date(quote.data.asOfUtc).toLocaleString(locale)}
                  </p>
                </>
              )}
            </div>
          </div>
          <div className="stock-details-actions">
            <button
              aria-pressed={isWatchlisted}
              className="stock-outline-button"
              disabled
              title={t("businessData.unavailable")}
              type="button"
            >
              <MarketIcon filled={isWatchlisted} name="star" size={15} />{" "}
              {isWatchlisted
                ? t("stockDetails.inWatchlist")
                : t("stockDetails.addToWatchlist", { symbol })}
            </button>
            <button
              className="stock-outline-button"
              type="button"
              disabled
              title={t("businessData.unavailable")}
            >
              {t("alerts.createAlert")}
            </button>
          </div>
        </header>
        <nav
          aria-label={t("stockDetails.sectionsLabel")}
          className="stock-detail-tabs"
        >
          {detailTabs.map((tab) => (
            <button
              key={tab}
              type="button"
              aria-pressed={tab === activeTab}
              className={tab === activeTab ? "stock-detail-tab-active" : ""}
              onClick={() => setActiveTab(tab)}
            >
              {t(`stockDetails.tabs.${tab}`)}
            </button>
          ))}
        </nav>
        {!["overview", "chart"].includes(activeTab) ? (
          <section className="stock-chart-card">
            <p>
              {t(
                activeTab === "aiInsights"
                  ? "marketApi.aiUnavailable"
                  : "marketApi.notAvailable",
              )}
            </p>
          </section>
        ) : (
          <div className="stock-details-layout">
            <div className="stock-details-main-column">
              <article className="stock-chart-card">
                <div className="stock-card-heading stock-chart-heading">
                  <div className="stock-card-title">
                    <MarketIcon name="chart" size={16} />
                    <h2>{t("stockDetails.priceChart")}</h2>
                  </div>
                  <div
                    aria-label={t("stockDetails.chartTimeRange")}
                    className="stock-range-tabs"
                  >
                    {chartRanges.map((range) => (
                      <button
                        key={range}
                        type="button"
                        title={
                          range === "MAX" ? t("marketApi.maxWindow") : undefined
                        }
                        aria-pressed={activeRange === range}
                        className={
                          activeRange === range ? "stock-range-active" : ""
                        }
                        onClick={() => setActiveRange(range)}
                      >
                        {t(`common.timeRanges.${range}`)}
                      </button>
                    ))}
                  </div>
                </div>
                <div className="stock-price-chart">
                  <MarketRequestStatus {...history} />
                  {history.data && !points.length && (
                    <p role="status">{t("marketApi.noHistory")}</p>
                  )}
                  {points.length > 0 && (
                    <FinancialLineChart
                      key={`${symbol}:${activeRange}`}
                      values={points.map((p) => p.value)}
                      labels={points.map((p) => p.label)}
                      min={Math.min(...points.map((p) => p.value)) * 0.99}
                      max={Math.max(...points.map((p) => p.value)) * 1.01}
                      ariaLabel={t("stockDetails.historicalPriceChart", {
                        symbol,
                        range: activeRange,
                      })}
                      formatValue={formatPrice}
                      showLatestValue
                    />
                  )}
                </div>
              </article>
              {!showEnrichment && (
                <button
                  className="stock-outline-button"
                  onClick={() => setShowEnrichment(true)}
                  type="button"
                >
                  {t("marketApi.loadInsights")}
                </button>
              )}
              <section
                aria-label={t("stockDetails.keyStatistics")}
                className="stock-stats-grid"
              >
                {[
                  "marketCap",
                  "peRatio",
                  "eps",
                  "dividendYield",
                  "nextEarnings",
                ].map((key) => (
                  <StatCard
                    key={key}
                    label={t(`stockDetails.stats.${key}`)}
                    value={
                      key === "nextEarnings"
                        ? (earnings.data?.nextEarningsDate ?? "—")
                        : key === "marketCap"
                          ? fundamentals.data?.marketCap == null
                            ? "—"
                            : new Intl.NumberFormat(locale, {
                                notation: "compact",
                                maximumFractionDigits: 2,
                              }).format(fundamentals.data.marketCap)
                          : key === "peRatio"
                            ? (fundamentals.data?.peRatio?.toLocaleString(
                                locale,
                              ) ?? "—")
                            : key === "eps"
                              ? formatPrice(fundamentals.data?.epsTtm)
                              : key === "dividendYield" &&
                                  fundamentals.data?.dividendYield != null
                                ? new Intl.NumberFormat(locale, {
                                    style: "percent",
                                    maximumFractionDigits: 2,
                                  }).format(fundamentals.data.dividendYield)
                                : "—"
                    }
                  />
                ))}
                <StatCard
                  label={t("stockDetails.stats.weekRange")}
                  value={`${formatPrice(quote.data?.fiftyTwoWeek?.low ?? fundamentals.data?.fiftyTwoWeekLow)} – ${formatPrice(quote.data?.fiftyTwoWeek?.high ?? fundamentals.data?.fiftyTwoWeekHigh)}`}
                />
                <StatCard
                  label={t("stockDetails.stats.volume")}
                  value={
                    quote.data?.volume == null
                      ? "—"
                      : formatNumber(quote.data.volume, i18n.language, 0)
                  }
                />
                <StatCard
                  label={t("marketApi.averageVolume")}
                  value={
                    quote.data?.averageVolume == null
                      ? "—"
                      : formatNumber(quote.data.averageVolume, i18n.language, 0)
                  }
                />
              </section>
              <MarketRequestStatus {...fundamentals} />
              <MarketRequestStatus {...earnings} />
              <section className="stock-metrics-card">
                {(["open", "high", "low"] as const).map((key) => (
                  <StatCard
                    metric
                    key={key}
                    label={t(`stockDetails.metrics.${key}`)}
                    value={formatPrice(quote.data?.[key])}
                  />
                ))}
                <StatCard
                  metric
                  label={t("stockDetails.metrics.previousClose")}
                  value={formatPrice(quote.data?.previousClose)}
                />
                {["beta", "analystRating", "analystTarget"].map((key) => (
                  <StatCard
                    metric
                    key={key}
                    label={t(`stockDetails.metrics.${key}`)}
                    value={
                      key === "beta"
                        ? (fundamentals.data?.beta?.toLocaleString(locale) ??
                          "—")
                        : key === "analystTarget"
                          ? formatPrice(fundamentals.data?.analystTargetPrice)
                          : fundamentals.data?.analystRatings
                            ? Object.entries(fundamentals.data.analystRatings)
                                .filter(([, count]) => count != null)
                                .map(
                                  ([name, count]) =>
                                    `${t(`marketApi.ratings.${name}`)} ${count}`,
                                )
                                .join(" · ") || "—"
                            : "—"
                    }
                  />
                ))}
              </section>
            </div>
            <aside className="stock-details-side-column">
              <article className="stock-ai-card">
                <div className="stock-card-heading">
                  <div className="stock-card-title">
                    <MarketIcon name="robot" size={16} />
                    <h2>{t("stockDetails.aiInsight")}</h2>
                  </div>
                </div>
                <p className="stock-ai-copy">{t("marketApi.aiUnavailable")}</p>
              </article>
              {!details && (
                <article className="stock-trade-card">
                  <h2>{t("stockDetails.paperTrading")}</h2>
                  <p>{t("marketApi.notAvailable")}</p>
                  <button
                    className="stock-primary-button"
                    type="button"
                    disabled
                  >
                    {t("common.buy")}
                  </button>
                </article>
              )}
              {details && (
                <TradeTicket
                  details={details}
                  limitPrice={limitPrice}
                  limitPriceError={limitPriceError}
                  onLimitPriceChange={(value) => {
                    setLimitPrice(value);
                    setLimitPriceError("");
                  }}
                  onOrderTypeChange={setOrderType}
                  onQuantityChange={(value) => {
                    setQuantity(value);
                    setQuantityError("");
                  }}
                  onSideChange={setTradeSide}
                  onSubmit={submitTrade}
                  orderType={orderType}
                  quantity={quantity}
                  quantityError={quantityError}
                  side={tradeSide}
                />
              )}
            </aside>
          </div>
        )}
      </section>
      {toast && (
        <div aria-live="polite" className="stock-toast">
          {t(toast.key, {
            ...toast.values,
            ...(toast.tabKey ? { tab: t(toast.tabKey) } : {}),
            ...(toast.side
              ? { side: t(toast.side === "BUY" ? "common.buy" : "common.sell") }
              : {}),
            ...(toast.orderType
              ? {
                  orderType: t(
                    toast.orderType === "limit"
                      ? "stockDetails.limitOrder"
                      : "stockDetails.marketOrder",
                  ),
                }
              : {}),
          })}
        </div>
      )}
    </MarketShell>
  );
}
