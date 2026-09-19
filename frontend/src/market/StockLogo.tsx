import "./stock-logo.css";
import { useState } from "react";
import { useTranslation } from "react-i18next";
export function StockLogo({
  symbol,
  size = "small",
}: {
  symbol: string;
  size?: "small" | "large";
}) {
  const { t } = useTranslation();
  const [failedUrl, setFailedUrl] = useState<string>();
  // The symbol endpoint uses US tickers. Do not turn a foreign exchange-qualified
  // symbol into a different US company that happens to use the same ticker.
  const parts = symbol.trim().toUpperCase().split(":");
  const [ticker, exchange] = parts;
  const supported = parts.length <= 2 && /^[A-Z][A-Z0-9.-]{0,14}$/.test(ticker) &&
    (!exchange || ["NASDAQ", "NYSE", "AMEX", "NYSEARCA", "NYSE ARCA", "BATS"].includes(exchange));
  const url = supported ? `https://api.elbstream.com/logos/symbol/${encodeURIComponent(ticker)}` : undefined;
  return (
    <span
      className={`market-stock-logo market-stock-logo-${size} ${url && failedUrl !== url ? "market-stock-logo-image" : ""}`}
      title={url && failedUrl !== url ? symbol : t("marketApi.logoUnavailable", { symbol })}
    >
      {url && failedUrl !== url ? (
        <img
          src={url}
          alt={symbol}
          loading="lazy"
          decoding="async"
          referrerPolicy="no-referrer"
          width={size === "large" ? 52 : 32}
          height={size === "large" ? 52 : 32}
          style={{ objectFit: "contain", maxWidth: "100%", maxHeight: "100%" }}
          onError={() => setFailedUrl(url)}
        />
      ) : (
        symbol.split(":")[0].slice(0, 5)
      )}
    </span>
  );
}

export function LogoAttribution() {
  const { t } = useTranslation();
  return <footer className="stock-logo-attribution"><a href="https://elbstream.com" target="_blank" rel="noreferrer">{t("marketApi.logoAttribution")}</a></footer>;
}
