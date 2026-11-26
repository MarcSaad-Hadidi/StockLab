import assert from "node:assert/strict";
import { test } from "node:test";
import {
  createMarketDataApi,
  type StockHistory,
} from "../src/api/marketDataApi.ts";
import {
  historyQuery,
  stockChartSeries,
  money,
  calculateTradeTotal,
  getTradeExecutionPrice,
  chartRanges,
} from "../src/market/stockDetailsData.ts";
const now = new Date("2026-09-08T12:30:00Z");
test('estimates preserve the exact normalized eight-by-four decimal product', () => {
  assert.equal(calculateTradeTotal('1.2345', '0.5'), '0.61725');
  assert.equal(calculateTradeTotal('0.0001', '0.00000001'), '0.000000000001');
  assert.equal(calculateTradeTotal('1.0001', '10000.00000001'), '10001.000000010001');
  assert.equal(calculateTradeTotal('1.23445', '0.500000005'), '0.617250012345');
  const wideTotal = calculateTradeTotal('9999999999999999999', '99999999999.99999999');
  assert.equal(wideTotal, '999999999999999999800000000000.00000001');
  assert.equal(money(wideTotal, 'USD', 'en-US', 12), '$999,999,999,999,999,999,800,000,000,000.00000001');
});
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
test("all enabled ranges have honest half-open boundaries; MAX uses bounded daily bars across splits", () => {
  for (const range of chartRanges.filter((r) => r !== "MAX")) {
    const query = historyQuery(range, now)!;
    assert.ok(query.from < query.to);
    if (["1D", "5D"].includes(range)) assert.ok(query.from.endsWith("Z"));
    else assert.match(query.from, /^\d{4}-\d{2}-\d{2}$/);
  }
  assert.equal(historyQuery("MAX", now)?.interval, "Day");
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
  const daily = stockChartSeries(data, "3M", "en-US");
  assert.deepEqual(daily.values, [204]);
  assert.deepEqual(daily.labels, ["Sep 7, 2026"]);
  assert.deepEqual(daily.tickIndexes, [0]);
  assert.deepEqual(stockChartSeries({ ...data, bars: [] }, "3M", "fr-FR").values, []);
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
  const point = stockChartSeries(intraday, "1D", "en-US");
  assert.match(point.labels[0], /2:00 PM UTC$/);
  assert.equal(point.ticks.get(0), "2:00 PM");
});

test("MAX reads actual daily prices across a split instead of accepting an inconsistent monthly aggregate", async () => {
  const splitBars = [
    { openTimeUtc: null, periodDate: '2014-06-06', open: 649.9, high: 651.26, low: 644.47, close: 645.57, volume: 1000 },
    { openTimeUtc: null, periodDate: '2014-06-09', open: 92.7, high: 93.88, low: 91.75, close: 93.7, volume: 2000 },
  ];
  let calls = 0;
  const api = createMarketDataApi('', async url => {
    calls++;
    assert.equal(new URL(String(url), 'http://localhost').searchParams.get('interval'), 'Day');
    return Response.json({ symbol: 'AAPL', currency: 'USD', interval: 'Day', bars: splitBars });
  });
  const history = await api.history('AAPL', historyQuery('MAX', now)!, new AbortController().signal, 'MAX');
  const plotted = stockChartSeries(history, 'MAX', 'en-US');
  assert.deepEqual(plotted.values, [645.57, 93.7]);
  assert.deepEqual(plotted.labels, ['Jun 6, 2014', 'Jun 9, 2014']);
  assert.equal(calls, 1);
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
  assert.equal(calculateTradeTotal('204.5', '10'), '2045');
  assert.equal(calculateTradeTotal('204.5', '-1'), '0');
  assert.equal(getTradeExecutionPrice("market", "204.5"), "204.5");
  assert.equal(getTradeExecutionPrice("limit", "204.5", "200"), "200");
});

test("trade estimates and currency formatting preserve four-decimal prices and totals", () => {
  assert.equal(calculateTradeTotal('0.0001', '1'), '0.0001');
  assert.equal(calculateTradeTotal('1.2345', '1.5'), '1.85175');
  assert.equal(calculateTradeTotal('0.0001', '0.5'), '0.00005');
  assert.equal(money(0.0001, "USD", "en-US", 4), "$0.0001");
  assert.equal(money(1.2345, "USD", "en-US", 4), "$1.2345");
  assert.match(money(1.2345, "USD", "fr-CA", 4), /1,2345/);
  assert.equal(money(150.25, "USD", "en-US", 4), "$150.25");
  assert.equal(money(1.2345, "USD", "en-US"), "$1.23");
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

test("all nine ranges send distinct bounded queries and symbols never share history", async () => {
  const requests: string[] = [];
  const api = createMarketDataApi("", async url => {
    requests.push(String(url));
    const parsed = new URL(String(url), "http://localhost");
    return Response.json({ ...data, symbol: parsed.pathname.includes("TSLA") ? "TSLA" : "AAPL",
      interval: parsed.searchParams.get("interval"), bars: [] });
  });
  const signal = new AbortController().signal;
  const expected = [
    ["1D", "Minute", "2026-09-07T12:30:00.000Z", "2026-09-08T12:30:00.000Z"],
    ["5D", "Hour", "2026-09-03T12:30:00.000Z", "2026-09-08T12:30:00.000Z"],
    ["1M", "Day", "2026-08-08", "2026-09-09"],
    ["3M", "Day", "2026-06-08", "2026-09-09"],
    ["6M", "Day", "2026-03-08", "2026-09-09"],
    ["YTD", "Day", "2026-01-01", "2026-09-09"],
    ["1Y", "Day", "2025-09-08", "2026-09-09"],
    ["5Y", "Week", "2021-09-08", "2026-09-09"],
    ["MAX", "Day", "2013-01-01", "2026-09-09"],
  ];
  for (const [range, interval, from, to] of expected) {
    await api.history("TSLA", historyQuery(range as typeof chartRanges[number], now)!, signal);
    const parsed = new URL(requests.at(-1)!, "http://localhost");
    assert.equal(parsed.searchParams.get("interval"), interval);
    assert.equal(parsed.searchParams.get("from"), from);
    assert.equal(parsed.searchParams.get("to"), to);
  }
  assert.equal(new Set(requests).size, 9);
  await api.history("TSLA", historyQuery("1M", now)!, signal);
  assert.equal(requests.length, 9);
  const apple = await api.history("AAPL", historyQuery("1M", now)!, signal);
  assert.equal(apple.symbol, "AAPL");
  assert.equal(requests.length, 10);
});

test("reopening the same symbol and range reuses history until its TTL expires", async () => {
  let calls = 0;
  let time = now.getTime();
  const originalNow = Date.now;
  Date.now = () => time;
  const api = createMarketDataApi("", async url => {
    calls++;
    return Response.json({ ...data, symbol: String(url).includes("TSLA") ? "TSLA" : "AAPL", interval: "Hour", bars: [] });
  });
  const signal = new AbortController().signal;
  try {
    await api.history("TSLA", historyQuery("5D", new Date(time))!, signal, "5D");
    time += 60_000;
    await api.history("TSLA", historyQuery("5D", new Date(time))!, signal, "5D");
    assert.equal(calls, 1);
    await api.history("AAPL", historyQuery("5D", new Date(time))!, signal, "5D");
    assert.equal(calls, 2);
    time += 300_000;
    await api.history("TSLA", historyQuery("5D", new Date(time))!, signal, "5D");
    assert.equal(calls, 3);
  } finally { Date.now = originalNow; }
});


test("dense intraday keeps all bars while the period changes the label cadence and format", () => {
  const history: StockHistory = { ...data, interval: "Minute",
    bars: Array.from({ length: 1200 }, (_, index) => ({
      ...data.bars[0], periodDate: null, close: 100 + index / 100,
      openTimeUtc: new Date(Date.UTC(2026, 10, 24, 3, index)).toISOString(),
    })) };
  const day = stockChartSeries(history, "1D", "en-CA");
  const week = stockChartSeries({ ...history, interval: "Hour", bars: history.bars.slice(0, 120).map((bar, index) => ({
    ...bar, openTimeUtc: new Date(Date.UTC(2026, 10, 20, index)).toISOString(),
  })) }, "5D", "en-CA");
  assert.equal(day.values.length, 1200);
  assert.equal(day.labels.length, 1200);
  assert.equal(day.tickIndexes.length, 5);
  assert.equal(week.tickIndexes.length, 5);
  assert.equal(new Set(week.ticks.values()).size, 5);
  assert.deepEqual([day.tickIndexes[0], day.tickIndexes.at(-1)], [0, 1199]);
  assert.notEqual(day.ticks.get(0), week.ticks.get(0));
  assert.match(stockChartSeries(history, "1D", "fr-CA").labels[0], /03 h 00 UTC$/);
});
