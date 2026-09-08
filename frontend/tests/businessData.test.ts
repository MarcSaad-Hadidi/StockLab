import assert from "node:assert/strict";
import { test } from "node:test";
import { registerHooks } from "node:module";
import { JSDOM } from "jsdom";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import * as dashboard from "../src/dashboard/dashboardData.ts";
import * as portfolio from "../src/portfolio/portfolioData.ts";
import * as transactions from "../src/transactions/transactionsData.ts";
import * as watchlist from "../src/watchlist/watchlistData.ts";
import * as alerts from "../src/alerts/alertsData.ts";
import * as ai from "../src/ai-trader/aiTraderData.ts";
import {
  formatCurrency,
  formatSignedCurrency,
  formatPercent,
  formatNumber,
} from "../src/i18n/formatters.ts";

registerHooks({
  load(url, context, nextLoad) {
    if (url.endsWith(".css"))
      return {
        format: "module",
        shortCircuit: true,
        source: "export default {}",
      };
    return nextLoad(url, context);
  },
});
const dom = new JSDOM("", { url: "http://localhost/dashboard" });
Object.assign(globalThis, {
  React,
  window: dom.window,
  document: dom.window.document,
});

test("unconnected business services contain no manufactured holdings, orders, alerts or AI results", () => {
  for (const rows of [
    dashboard.positions,
    dashboard.watchlist,
    dashboard.transactions,
    portfolio.positions,
    transactions.transactions,
    watchlist.watchlistItems,
    alerts.initialAlerts,
    alerts.assetOptions,
    ai.positions,
    ai.currentDecisions,
    ai.rejectedDecisions,
    ai.recentTrades,
    ai.modelHistory,
  ])
    assert.deepEqual(rows, []);
  for (const metric of dashboard.metrics) {
    assert.equal(metric.value, null);
    assert.equal(metric.change, null);
  }
  for (const series of [
    ...Object.values(dashboard.performanceSeries),
    ...Object.values(portfolio.performanceSeries),
  ])
    assert.deepEqual(series.values, []);
  assert.deepEqual(ai.performanceSeries, []);
  assert.equal(ai.currentModel, null);
  assert.ok(Object.values(ai.traderSummary).every((value) => value === null));
  assert.ok(Object.values(ai.backtestSummary).every((value) => value === null));
  assert.equal(transactions.transactionSummary.netPnl, null);
});

test("unavailable financial values stay unavailable rather than becoming zero or NaN", () => {
  for (const formatter of [
    formatCurrency,
    formatSignedCurrency,
    formatPercent,
    formatNumber,
  ]) {
    for (const missing of [null, undefined, Number.NaN])
      assert.equal(formatter(missing), "—");
  }
});

for (const [path, label] of [
  ["dashboard/DashboardPage", "No portfolio data yet"],
  ["portfolio/PortfolioPage", "No portfolio data yet"],
  ["transactions/TransactionsPage", "No transactions"],
  ["watchlist/WatchlistPage", "No watchlist items"],
  ["alerts/AlertsPage", "No alerts"],
  ["ai-trader/AITraderPage", "AI Trader is not available yet"],
] as const)
  test(`${path} renders its original sections without fake business data`, async () => {
    const { i18n } = await import("../src/i18n/i18n.ts");
    await i18n.changeLanguage("en");
    const module = await import(`../src/${path}.tsx`);
    const Page = module.default ?? module[path.split("/").at(-1)!];
    const html = renderToStaticMarkup(React.createElement(Page));
    assert.ok(html.includes(label), `Missing empty state: ${label}`);
    assert.match(html, /<main/);
    assert.match(html, /<nav/);
    assert.doesNotMatch(
      html,
      /191\.45|415\.60|892\.72|154\.32|48,294|128,547|v3\.2\.1|2024-05|<polyline|<canvas/,
    );
  });
