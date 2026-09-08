import "./stock-logo.css";
import { useCallback, useState, useSyncExternalStore } from "react";
import { marketDataApi } from "../api/marketDataClient";
import { useMarketRequest } from "./useMarketRequest";
export function StockLogo({
  symbol,
  size = "small",
  load = true,
}: {
  symbol: string;
  size?: "small" | "large";
  load?: boolean;
}) {
  const request = useCallback(
    (signal: AbortSignal) => marketDataApi.logo(symbol, signal),
    [symbol],
  );
  const logo = useMarketRequest(symbol, request, load);
  const [failedUrl, setFailedUrl] = useState<string>();
  const cached = useSyncExternalStore(
    marketDataApi.subscribeLogos,
    () => marketDataApi.cachedLogo(symbol),
    () => undefined,
  );
  const available = logo.data ?? cached;
  const url = available?.pngUrl ?? available?.svgUrl;
  return (
    <span
      aria-hidden="true"
      className={`market-stock-logo market-stock-logo-${size} ${url && failedUrl !== url ? "market-stock-logo-image" : ""}`}
    >
      {url && failedUrl !== url ? (
        <img
          src={url}
          alt=""
          width={size === "large" ? 52 : 32}
          height={size === "large" ? 52 : 32}
          style={{ objectFit: "contain", maxWidth: "100%", maxHeight: "100%" }}
          onError={() => setFailedUrl(url)}
        />
      ) : (
        symbol.charAt(0)
      )}
    </span>
  );
}
