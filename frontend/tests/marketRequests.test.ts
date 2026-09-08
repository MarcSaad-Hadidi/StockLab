import { i18n } from "../src/i18n/i18n.ts";
import { registerHooks } from "node:module";

import assert from "node:assert/strict";
import { test } from "node:test";
import { JSDOM } from "jsdom";
import { createElement, act, StrictMode } from "react";

import { useMarketRequest } from "../src/market/useMarketRequest.ts";
import { createMarketDataApi } from "../src/api/marketDataApi.ts";
const dom = new JSDOM('<!doctype html><div id="root"></div>');
Object.assign(globalThis, {
  window: dom.window,
  document: dom.window.document,
  IS_REACT_ACT_ENVIRONMENT: true,
});
const { createRoot } = await import("react-dom/client");
registerHooks({
  load(url, context, nextLoad) {
    if (url.endsWith(".css"))
      return {
        format: "module",
        shortCircuit: true,
        source: "export default {}",
      };
    // This failure-path test must never render a chart; avoid loading the canvas library in Node.
    if (url.includes('/components/charts/FinancialLineChart')) return { format: 'module', shortCircuit: true, source: 'export function FinancialLineChart() { throw new Error("Chart rendered without history") }' };
    return nextLoad(url, context);
  },
});
const pause = (ms = 20) =>
  act(async () => {
    await new Promise((resolve) => setTimeout(resolve, ms));
  });
function Probe({
  query,
  load,
  enabled = true,
  delay = 0,
}: {
  query: string;
  load: (s: AbortSignal) => Promise<string>;
  enabled?: boolean;
  delay?: number;
}) {
  const state = useMarketRequest(query, load, enabled, delay);
  return createElement(
    "div",
    null,
    state.data ?? state.error ?? (state.loading ? "loading" : "idle"),
  );
}
test("debounce, StrictMode replay, stale response, empty query, and unmount cancellation", async () => {
  const root = createRoot(document.getElementById("root")!);
  const signals: AbortSignal[] = [];
  const pending: Array<(v: string) => void> = [];
  const load = (s: AbortSignal) => {
    signals.push(s);
    return new Promise<string>((resolve) => pending.push(resolve));
  };
  const render = async (query: string, enabled = true) =>
    act(async () => {
      root.render(
        createElement(
          StrictMode,
          null,
          createElement(Probe, { query, load, delay: 450, enabled }),
        ),
      );
    });
  try {
    await render("", false);
    await pause();
    assert.equal(signals.length, 0);
    await render("a");
    await pause(50);
    await render("ap");
    await pause(50);
    await render("apple");
    await pause(480);
    assert.equal(signals.length, 1);
    await render("microsoft");
    assert.equal(signals[0].aborted, true);
    await act(async () => pending[0]("stale apple"));
    assert.doesNotMatch(document.body.textContent!, /stale apple/);
    await pause(480);
    assert.equal(signals.length, 2);
    await act(async () => pending[1]("MSFT"));
    assert.match(document.body.textContent!, /MSFT/);
    await render("", false);
    assert.equal(document.body.textContent, "idle");
    await render("NVDA");
    await pause(480);
    assert.equal(signals.length, 3);
  } finally {
    await act(async () => root.unmount());
  }
  assert.equal(signals.at(-1)?.aborted, true);
});
test("independent quote and history failures preserve successful price; no polling", async () => {
  const root = createRoot(document.getElementById("root")!);
  const calls: string[] = [];
  const api = createMarketDataApi("", async (url) => {
    calls.push(String(url));
    return String(url).endsWith("/quote")
      ? Response.json({
          symbol: "AAPL",
          price: 204.5,
          change: 1,
          changePercent: 0.5,
          volume: 100,
          currency: "USD",
          asOfUtc: "2026-09-04T20:00:00Z",
          name: "Apple",
          exchange: "NASDAQ",
          open: null,
          high: null,
          low: null,
          previousClose: null,
          averageVolume: null,
          isMarketOpen: null,
          fiftyTwoWeek: null,
        })
      : new Response("", { status: 504 });
  });
  const loadQuote = async (s: AbortSignal) =>
    String((await api.quote("AAPL", s)).price);
  const loadHistory = async (s: AbortSignal) =>
    String(
      (
        await api.history(
          "AAPL",
          { from: "2026-06-01", to: "2026-09-08", interval: "Day" },
          s,
        )
      ).bars.length,
    );
  try {
    await act(async () =>
      root.render(
        createElement(
          "div",
          null,
          createElement(Probe, { query: "quote", load: loadQuote }),
          createElement(Probe, { query: "history", load: loadHistory }),
        ),
      ),
    );
    await pause();
    assert.match(document.body.textContent!, /204.5/);
    assert.match(document.body.textContent!, /marketApi.timeout/);
    await pause(500);
    assert.equal(calls.length, 2);
  } finally {
    await act(async () => root.unmount());
  }
});

test("company logos load automatically and broken images retain the ticker fallback", async () => {
  // The node test harness uses classic JSX for imported components.
  Object.assign(globalThis, { React: await import("react") });
  const { StockLogo } = await import("../src/market/StockLogo.tsx");
  const { marketDataApi } = await import("../src/api/marketDataClient.ts");
  const original = marketDataApi.logo;
  let calls = 0;
  marketDataApi.logo = async (symbol) => {
    calls++;
    return {
      symbol,
      pngUrl: "https://cdn.alphavantage.co/logos/MSFT.png",
      svgUrl: null,
    };
  };
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  try {
    await act(async () =>
      root.render(createElement(StockLogo, { symbol: "MSFT" })),
    );
    await pause();
    assert.equal(calls, 1);
    const img = host.querySelector("img")!;
    assert.ok(img);
    await act(async () => img.dispatchEvent(new dom.window.Event("error")));
    assert.equal(host.textContent, "M");
    assert.equal(calls, 1);
  } finally {
    await act(async () => root.unmount());
    host.remove();
    marketDataApi.logo = original;
  }
});


test("Stock Details keeps a real supplied quote when enrichment/history fail and never restores a mock on quote failure", async () => {
  Object.assign(globalThis, { React: await import("react") });
  await i18n.changeLanguage("en");
  const { StockDetailsPage } = await import("../src/market/StockDetailsPage.tsx");
  const { marketDataApi } = await import("../src/api/marketDataClient.ts");
  const { MarketDataError } = await import("../src/api/marketDataApi.ts");
  const original = { quote: marketDataApi.quote, history: marketDataApi.history, logo: marketDataApi.logo, fundamentals: marketDataApi.fundamentals, earnings: marketDataApi.earnings };
  let quoteCalls = 0;
  marketDataApi.quote = async symbol => {
    quoteCalls++;
    if (symbol === "UNKNOWN") throw new MarketDataError(404);
    return { symbol, name: "Test Tesla", exchange: "NASDAQ", currency: "USD", price: 321.45, change: 1, changePercent: .3, volume: 100, asOfUtc: "2026-09-04T20:00:00Z", open: null, high: null, low: null, previousClose: null, averageVolume: null, isMarketOpen: false, fiftyTwoWeek: null };
  };
  marketDataApi.history = async () => { throw new MarketDataError(503); };
  marketDataApi.logo = async () => { throw new MarketDataError(503); };
  marketDataApi.fundamentals = async () => { throw new MarketDataError(503); };
  marketDataApi.earnings = async () => { throw new MarketDataError(503); };
  const root = createRoot(document.getElementById("root")!);
  try {
    await act(async () => root.render(createElement(StockDetailsPage, { requestedSymbol: "TSLA", onBack() {} })));
    await pause(120);
    assert.match(document.body.textContent!, /321.45/);
    assert.match(document.body.textContent!, /Test Tesla/);
    const loadInsights = Array.from(document.querySelectorAll('button')).find(button => button.textContent?.trim() === i18n.t('marketApi.loadInsights'));
    assert.ok(loadInsights);
    await act(async () => loadInsights.click());
    await pause(120);
    assert.match(document.body.textContent!, /321.45/);
    assert.ok(document.querySelectorAll('[role="alert"]').length >= 3);

    assert.equal(document.querySelector("canvas"), null);
    assert.ok(document.querySelector('.stock-trade-submit[disabled]'));
    await act(async () => root.render(createElement(StockDetailsPage, { requestedSymbol: "UNKNOWN", onBack() {} })));
    await pause(120);
    assert.doesNotMatch(document.body.textContent!, /321.45|191.45|415.60|892.72/);
    assert.equal(document.querySelector("canvas"), null);
    assert.equal(quoteCalls, 2);
  } finally {
    await act(async () => root.unmount());
    Object.assign(marketDataApi, original);
  }
});


test("alert draft search is debounced once and does not loop on result renders", async () => {
  Object.assign(globalThis, { React: await import("react") });
  const { default: AlertsPage } = await import("../src/alerts/AlertsPage.tsx");
  const { marketDataApi } = await import("../src/api/marketDataClient.ts");
  const original = marketDataApi.search;
  let calls = 0;
  marketDataApi.search = async () => { calls++; return [{ symbol: "TSLA", companyName: "Test Tesla", exchange: "NASDAQ", currency: "USD" }]; };
  const root = createRoot(document.getElementById("root")!);
  try {
    await act(async () => root.render(createElement(AlertsPage)));
    const open = Array.from(document.querySelectorAll('button')).find(button => button.textContent?.trim() === i18n.t('alerts.createAlert'))!;
    await act(async () => open.click());
    const input = document.querySelector('[role="dialog"] input[aria-label]') as HTMLInputElement;
    assert.ok(input);
    await act(async () => {
      Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, 'value')!.set!.call(input, 'tesla');
      input.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    });
    await pause(520);
    assert.match(document.querySelector('[role="dialog"]')!.textContent!, /Test Tesla/);
    assert.equal(calls, 1);
    await pause(1000);
    assert.equal(calls, 1);
    assert.ok(document.querySelector('[role="dialog"] button[type="submit"][disabled]'));
  } finally {
    await act(async () => root.unmount());
    marketDataApi.search = original;
  }
});
