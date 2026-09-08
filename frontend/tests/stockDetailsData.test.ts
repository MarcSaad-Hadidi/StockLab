import assert from "node:assert/strict";
import { test } from "node:test";
import {
  createMarketDataApi,
  type StockHistory,
} from "../src/api/marketDataApi.ts";
import {
  historyQuery,
  historyPoints,
  money,
  calculateTradeTotal,
  getTradeExecutionPrice,
  chartRanges,
} from "../src/market/stockDetailsData.ts";
const now = new Date("2026-09-08T12:30:00Z");
const data: StockHistory = {
  symbol: "AAPL",
  currency: "USD",
  interval: "Day",
  bars: [
    {
      openTimeUtc: null,
      periodDate: "2026-09-07",
      open: 200,
      high: 205,
      low: 199,
      close: 204,
      volume: 1000,
    },
  ],
};
test("all enabled ranges have honest half-open boundaries; MAX bounded monthly", () => {
  for (const range of chartRanges.filter((r) => r !== "MAX")) {
    const query = historyQuery(range, now)!;
    assert.ok(query.from < query.to);
    if (["1D", "5D"].includes(range)) assert.ok(query.from.endsWith("Z"));
    else assert.match(query.from, /^\d{4}-\d{2}-\d{2}$/);
  }
  assert.equal(historyQuery("MAX", now)?.interval, "Month");
  assert.equal(
    (Date.parse(historyQuery("MAX", now)!.to) -
      Date.parse(historyQuery("MAX", now)!.from)) /
      86400000,
    4999,
  );
  assert.equal(historyQuery("5Y", now)?.interval, "Week");
  assert.equal(historyQuery("YTD", now)?.from, "2026-01-01");
});
test("preserves calendar dates and genuine bar count without synthetic points", () => {
  assert.deepEqual(historyPoints(data, "en-US"), [
    { label: "2026-09-07", value: 204 },
  ]);
  assert.deepEqual(historyPoints({ ...data, bars: [] }, "fr-FR"), []);
  const intraday: StockHistory = {
    ...data,
    interval: "Hour",
    bars: [
      {
        ...data.bars[0],
        periodDate: null,
        openTimeUtc: "2026-09-07T14:00:00Z",
      },
    ],
  };
  assert.match(historyPoints(intraday, "en-US")[0].label, /UTC$/);
});
test("range switching fetches once per uncached range and no prefetch", async () => {
  const calls: string[] = [];
  const api = createMarketDataApi("", async (url) => {
    calls.push(String(url));
    return Response.json(data);
  });
  const signal = new AbortController().signal;
  for (const range of ["3M", "1Y", "3M"] as const)
    await api.history("AAPL", historyQuery(range, now)!, signal);
  assert.equal(calls.length, 2);
  assert.ok(calls.every((url) => url.includes("interval=Day")));
});
test("rejects mismatched temporal fields and malformed OHLCV", async () => {
  for (const bar of [
    { ...data.bars[0], openTimeUtc: "2026-09-07T00:00:00Z" },
    { ...data.bars[0], high: 100 },
    { ...data.bars[0], volume: -1 },
  ]) {
    const api = createMarketDataApi("", async () =>
      Response.json({ ...data, bars: [bar] }),
    );
    await assert.rejects(
      api.history(
        "AAPL",
        historyQuery("3M", now)!,
        new AbortController().signal,
      ),
    );
  }
});
test("currency comes from metadata/history, never assumed USD", () => {
  assert.equal(money(204.5, null, "en-US"), "204.50");
  assert.match(money(204.5, "EUR", "en-US"), /€/);
  assert.equal(money(null, "USD", "en-US"), "—");
});
test("local trade estimates use supplied market or limit price", () => {
  assert.equal(calculateTradeTotal(204.5, 10), 2045);
  assert.equal(calculateTradeTotal(204.5, -1), 0);
  assert.equal(getTradeExecutionPrice("market", 204.5), 204.5);
  assert.equal(getTradeExecutionPrice("limit", 204.5, 200), 200);
});

test("calendar month windows clamp month ends without rolling into the next month", () => {
  assert.equal(
    historyQuery("1M", new Date("2026-03-31T12:00:00Z"))?.from,
    "2026-02-28",
  );
  assert.equal(
    historyQuery("1Y", new Date("2024-02-29T12:00:00Z"))?.from,
    "2023-02-28",
  );
});
