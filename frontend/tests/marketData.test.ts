import assert from "node:assert/strict";
import { test } from "node:test";
import {
  createMarketDataApi,
  errorKey,
  MarketDataError,
} from "../src/api/marketDataApi.ts";
import {
  featuredSymbols,
  loadFeaturedQuotes,
} from "../src/market/marketData.ts";
import { stockDetailsRoute } from "../src/market/marketRoutes.ts";
const signal = () => new AbortController().signal;
const quote = {
  symbol: "AAPL",
  price: 204.5,
  change: null,
  changePercent: null,
  volume: null,
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
};
test("curated list has only five identifiers; routes preserve exchange suffixes", () => {
  assert.deepEqual(featuredSymbols, ["AAPL", "MSFT", "NVDA", "AMZN", "GOOGL"]);
  assert.equal(
    stockDetailsRoute("TSLA:NASDAQ"),
    "/market?symbol=tsla%3Anasdaq",
  );
});
test("encodes symbols and queries, sends cancellation, never requests search result quotes", async () => {
  const calls: string[] = [];
  const token = signal();
  const api = createMarketDataApi(
    "http://localhost:5274/",
    async (url, options) => {
      calls.push(String(url));
      assert.equal(options?.signal, token);
      return Response.json(
        String(url).includes("/search?")
          ? [
              {
                symbol: "BRK.B:NYSE",
                companyName: "Berkshire",
                exchange: "NYSE",
                currency: null,
              },
            ]
          : quote,
      );
    },
  );
  await api.quote(" brk.b:nyse ", token);
  await api.search("a & b", token);
  assert.deepEqual(calls, [
    "http://localhost:5274/api/stocks/BRK.B%3ANYSE/quote",
    "http://localhost:5274/api/stocks/search?query=a+%26+b",
  ]);
  assert.equal(api.metadata("BRK.B:NYSE")?.companyName, "Berkshire");
});
test("empty query and previously cached search generate no requests", async () => {
  let count = 0;
  const api = createMarketDataApi("", async () => {
    count++;
    return Response.json([]);
  });
  assert.deepEqual(await api.search("  ", signal()), []);
  await api.search("unknown", signal());
  await api.search("unknown", signal());
  assert.equal(count, 1);
});
for (const [status, key] of [
  [400, "invalid"],
  [404, "notFound"],
  [429, "rateLimited"],
  [500, "serverError"],
  [502, "invalidResponse"],
  [503, "unavailable"],
  [504, "timeout"],
] as const) {
  test(`safe ${status} message, no automatic retry or cached error`, async () => {
    let count = 0;
    const api = createMarketDataApi("", async () => {
      count++;
      return new Response("technical provider details", { status });
    });
    await assert.rejects(
      api.quote("AAPL", signal()),
      (error) =>
        error instanceof MarketDataError &&
        errorKey(error) === `marketApi.${key}` &&
        !error.message.includes("technical"),
    );
    assert.equal(count, 1);
    await assert.rejects(api.quote("AAPL", signal()));
    assert.equal(count, 2);
  });
}
test("malformed numeric quote rejected; null optional fields retained", async () => {
  const valid = createMarketDataApi("", async () => Response.json(quote));
  assert.deepEqual(await valid.quote("AAPL", signal()), quote);
  for (const bad of [
    { ...quote, price: "204" },
    { ...quote, volume: -1 },
    { ...quote, price: 0 },
  ]) {
    const api = createMarketDataApi("", async () => Response.json(bad));
    await assert.rejects(
      api.quote("AAPL", signal()),
      (error) => error instanceof MarketDataError && error.status === 502,
    );
  }
});
test("network failure is safe; already cancelled request has zero calls", async () => {
  let count = 0;
  const api = createMarketDataApi("", async () => {
    count++;
    throw Error("network internals");
  });
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(api.quote("AAPL", controller.signal), {
    name: "AbortError",
  });
  assert.equal(count, 0);
  await assert.rejects(
    api.quote("AAPL", signal()),
    (error) =>
      error instanceof MarketDataError &&
      errorKey(error) === "marketApi.offline",
  );
});
test("aborted late response is discarded and not cached", async () => {
  let complete!: (value: Response) => void;
  let count = 0;
  const api = createMarketDataApi("", () => {
    count++;
    return new Promise((resolve) => {
      complete = resolve;
    });
  });
  const controller = new AbortController();
  const request = api.quote("AAPL", controller.signal);
  controller.abort();
  complete(Response.json(quote));
  await assert.rejects(request, { name: "AbortError" });
  const next = api.quote("AAPL", signal());
  complete(Response.json(quote));
  await next;
  assert.equal(count, 2);
});

test("five popular quotes use at most three concurrent calls and tolerate one failure", async () => {
  let active = 0,
    maximum = 0,
    calls = 0;
  const rows = await loadFeaturedQuotes(async (symbol) => {
    calls++;
    active++;
    maximum = Math.max(maximum, active);
    await new Promise((resolve) => setTimeout(resolve, 5));
    active--;
    if (symbol === "NVDA") throw new Error("offline");
    return { ...quote, symbol };
  }, signal());
  assert.equal(calls, 5);
  assert.equal(maximum, 3);
  assert.equal(rows.filter((row) => row.quote).length, 4);
  assert.equal(rows.find((row) => row.symbol === "NVDA")?.failed, true);
});
test("enrichment uses StockLab only, has independent long caches and safe failures", async () => {
  const calls: string[] = [];
  const api = createMarketDataApi("", async (url) => {
    calls.push(String(url));
    if (String(url).endsWith("/fundamentals"))
      return new Response("", { status: 503 });
    return Response.json({ symbol: "MSFT", pngUrl: null, svgUrl: null });
  });
  await assert.rejects(api.fundamentals("MSFT", signal()));
  assert.equal((await api.logo("MSFT", signal())).pngUrl, null);
  await api.logo("MSFT", signal());
  assert.deepEqual(calls, [
    "/api/stocks/MSFT/fundamentals",
    "/api/stocks/MSFT/logo",
  ]);
});

test("cached logo consumers are notified without issuing their own external request", async () => {
  let calls = 0;
  let notifications = 0;
  const api = createMarketDataApi("", async () => {
    calls++;
    return Response.json({
      symbol: "MSFT",
      pngUrl: "https://cdn.alphavantage.co/logos/MSFT.png",
      svgUrl: null,
    });
  });
  const unsubscribe = api.subscribeLogos(() => notifications++);
  assert.equal(api.cachedLogo("MSFT"), undefined);
  await api.logo("MSFT", signal());
  assert.equal(api.cachedLogo("msft")?.symbol, "MSFT");
  assert.equal(notifications, 1);
  assert.equal(calls, 1);
  await api.logo("MSFT", signal());
  assert.equal(calls, 1);
  assert.equal(notifications, 1);
  unsubscribe();
});


test("one movers response fills all lists and invalid mover numerics are rejected", async () => {
  let calls = 0;
  const row = { symbol: "TSLA", price: 123.4, change: 2.3, changePercent: 1.9, volume: 12345 };
  const payload = { lastUpdated: "2026-09-04", gainers: [row], losers: [], mostActive: [row] };
  const api = createMarketDataApi("", async () => { calls++; return Response.json(payload); });
  assert.deepEqual(await api.movers(signal()), payload);
  assert.deepEqual(await api.movers(signal()), payload);
  assert.equal(calls, 1);
  const invalid = createMarketDataApi("", async () => Response.json({ ...payload, gainers: [{ ...row, volume: null }] }));
  await assert.rejects(invalid.movers(signal()), MarketDataError);
});

test("logo cache lasts thirty days with no repeated request during its lifetime", async () => {
  const originalNow = Date.now;
  let time = originalNow(); let calls = 0;
  Date.now = () => time;
  try {
    const api = createMarketDataApi("", async () => { calls++; return Response.json({ symbol: "MSFT", pngUrl: null, svgUrl: null }); });
    await api.logo("MSFT", signal());
    time += 29 * 86_400_000;
    await api.logo("MSFT", signal());
    assert.equal(calls, 1);
    time += 2 * 86_400_000;
    await api.logo("MSFT", signal());
    assert.equal(calls, 2);
  } finally { Date.now = originalNow; }
});
