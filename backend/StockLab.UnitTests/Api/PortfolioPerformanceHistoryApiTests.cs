using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Portfolios;
using StockLab.Domain.Entities;

namespace StockLab.UnitTests.Api;

public sealed partial class PortfolioApiTests
{
    [Fact]
    public async Task Personal_history_requires_an_authenticated_session()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        using var response = await fixture.Client.GetAsync("/api/portfolio/performance/history");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Personal_history_is_empty_until_observations_are_persisted_and_never_fetches_quotes()
    {
        var provider = new CountingMarketDataProvider();
        await using var fixture = await PortfolioFixture.CreateAsync(provider);
        var account = await CreateSignedInAccountAsync(fixture, "empty-personal-history@example.com");
        await SeedPortfolioAsync(fixture, account.Id, 90000m, "CAD", ("MSFT", 1m, 100m));

        foreach (var range in new[] { "1D", "1W", "1M", "3M", "1Y", "YTD", "ALL" })
        {
            using var response = await GetWithTokenAsync(fixture.Client, account.Token,
                $"/api/portfolio/performance/history?range={range}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(range, body.RootElement.GetProperty("range").GetString());
            Assert.Equal("CAD", body.RootElement.GetProperty("currency").GetString());
            Assert.Equal(0, body.RootElement.GetProperty("sampleCount").GetInt32());
            Assert.Empty(body.RootElement.GetProperty("points").EnumerateArray());
        }
        Assert.Equal(0, provider.QuoteCalls);
    }

    [Theory]
    [InlineData("2Y")]
    [InlineData("all")]
    [InlineData("")]
    public async Task Personal_history_rejects_unknown_ranges(string range)
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "bad-history-range@example.com");
        using var response = await GetWithTokenAsync(fixture.Client, account.Token,
            $"/api/portfolio/performance/history?range={range}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Personal_history_defaults_to_one_month_and_does_not_treat_a_missing_portfolio_as_empty()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "default-history-range@example.com");
        using (var response = await GetWithTokenAsync(fixture.Client, account.Token,
            "/api/portfolio/performance/history"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("1M", body.RootElement.GetProperty("range").GetString());
        }
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            db.Portfolios.Remove(await db.Portfolios.SingleAsync(row => row.UserId == account.Id));
            await db.SaveChangesAsync();
        }
        using var missing = await GetWithTokenAsync(fixture.Client, account.Token,
            "/api/portfolio/performance/history");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("portfolio_not_found", await ErrorCodeAsync(missing));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Personal_history_capture_persists_actual_exact_values_and_replays_the_original_slot(bool hasHolding)
    {
        var clock = new PersonalHistoryClock(DateTimeOffset.UtcNow);
        var provider = new CountingMarketDataProvider(1.2345m, clock.Now.AddMinutes(-1));
        await using var fixture = await PortfolioFixture.CreateAsync(provider, clock: clock);
        var account = await CreateSignedInAccountAsync(fixture, "captured-personal-history@example.com");
        await SeedPortfolioAsync(fixture, account.Id, 99999.999999999999m, "USD",
            hasHolding ? [("MSFT", 0.5m, 1m)] : []);
        using var scope = fixture.CreateScope();
        var capture = scope.ServiceProvider.GetRequiredService<PortfolioPerformanceCaptureService>();
        var first = await capture.CaptureAsync(account.Id, default);
        Assert.NotNull(first);
        Assert.Equal(hasHolding ? 100000.617249999999m : 99999.999999999999m, first.TotalValue);
        Assert.Equal(hasHolding ? "Mock" : "Cash", first.Source);
        Assert.True(first.RecordedAtUtc >= first.SlotUtc);
        Assert.Equal(first.Id, (await capture.CaptureAsync(account.Id, default))!.Id);
        Assert.Equal(hasHolding ? 1 : 0, provider.QuoteCalls);
        using var response = await GetWithTokenAsync(fixture.Client, account.Token,
            "/api/portfolio/performance/history?range=ALL");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var point = Assert.Single(body.RootElement.GetProperty("points").EnumerateArray());
        Assert.Equal(first.TotalValue.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture),
            point.GetProperty("totalValue").GetString());
        Assert.Equal(hasHolding ? 1 : 0, provider.QuoteCalls); // Reading history never refreshes market data.
        var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        Assert.Single(await db.PortfolioValuationSnapshots.ToArrayAsync());
        Assert.Empty(await db.Transactions.ToArrayAsync());
    }

    [Fact]
    public async Task Personal_history_scopes_ownership_currency_and_actual_UTC_period_boundaries()
    {
        var clock = new PersonalHistoryClock(DateTimeOffset.UtcNow);
        await using var fixture = await PortfolioFixture.CreateAsync(clock: clock);
        var account = await CreateSignedInAccountAsync(fixture, "owned-points@example.com");
        var other = await CreateSignedInAccountAsync(fixture, "other-points@example.com");
        var id = await SeedPortfolioAsync(fixture, account.Id, 100000m, "CAD");
        var otherId = await SeedPortfolioAsync(fixture, other.Id, 100000m, "CAD");
        var now = clock.Now.UtcDateTime;
        var from = now.AddMonths(-1);
        var owned = new[] { HistoryPoint(id, from.AddTicks(-1), 10m), HistoryPoint(id, from, 10000.000000000001m),
            HistoryPoint(id, now.AddHours(-2), 99999.999999999999m), HistoryPoint(id, now.AddTicks(1), 999m) };
        var wrongCurrency = HistoryPoint(id, now.AddHours(-1), 888m);
        wrongCurrency.Currency = "USD";
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            db.PortfolioValuationSnapshots.AddRange(owned);
            db.PortfolioValuationSnapshots.AddRange(wrongCurrency, HistoryPoint(otherId, now.AddHours(-3), 777m));
            await db.SaveChangesAsync();
        }
        using var response = await GetWithTokenAsync(fixture.Client, account.Token,
            $"/api/portfolio/performance/history?range=1M&userId={other.Id}&portfolioId={otherId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(from, root.GetProperty("fromUtc").GetDateTime());
        Assert.Equal(now, root.GetProperty("toUtc").GetDateTime());
        Assert.Equal("CAD", root.GetProperty("currency").GetString());
        Assert.Equal(2, root.GetProperty("sampleCount").GetInt32());
        Assert.Equal(new[] { "10000.000000000001", "99999.999999999999" }, root.GetProperty("points")
            .EnumerateArray().Select(point => point.GetProperty("totalValue").GetString()).ToArray());
    }

    [Theory]
    [InlineData(1, "hour")]
    [InlineData(60, "day")]
    [InlineData(1440, "month")]
    [InlineData(43200, "year")]
    public async Task Personal_history_downsamples_the_entire_period_to_original_observations(int minutes, string resolution)
    {
        var clock = new PersonalHistoryClock(DateTimeOffset.UtcNow);
        await using var fixture = await PortfolioFixture.CreateAsync(clock: clock);
        var account = await CreateSignedInAccountAsync(fixture, "bucketed-points@example.com");
        var id = await SeedPortfolioAsync(fixture, account.Id, 100000m, "CAD");
        var points = Enumerable.Range(0, 601).Select(index => HistoryPoint(id,
            clock.Now.UtcDateTime.AddMinutes((index - 600d) * minutes), 100000m + index / 1000m)).ToArray();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            db.PortfolioValuationSnapshots.AddRange(points);
            await db.SaveChangesAsync();
        }
        using var response = await GetWithTokenAsync(fixture.Client, account.Token,
            "/api/portfolio/performance/history?range=ALL");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(601, root.GetProperty("sampleCount").GetInt32());
        Assert.Equal(resolution, root.GetProperty("resolution").GetString());
        Assert.Equal(points[0].RecordedAtUtc, root.GetProperty("firstRecordedAtUtc").GetDateTime());
        Assert.Equal(points[^1].RecordedAtUtc, root.GetProperty("lastRecordedAtUtc").GetDateTime());
        var returned = root.GetProperty("points").EnumerateArray().ToArray();
        Assert.InRange(returned.Length, 2, 500);
        Assert.Equal(clock.Now.UtcDateTime, returned[^1].GetProperty("recordedAtUtc").GetDateTime());
        Assert.True(returned[0].GetProperty("recordedAtUtc").GetDateTime() < points[300].RecordedAtUtc);
        foreach (var point in returned)
        {
            var original = Assert.Single(points, row => row.RecordedAtUtc == point.GetProperty("recordedAtUtc").GetDateTime());
            Assert.Equal(original.TotalValue, decimal.Parse(point.GetProperty("totalValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Personal_history_provenance_and_production_policy_follow_the_frozen_provider_selection(bool production)
    {
        var clock = new PersonalHistoryClock(DateTimeOffset.UtcNow);
        var provider = new CountingMarketDataProvider(1m, clock.Now.AddMinutes(-1));
        await using var fixture = await PortfolioFixture.CreateAsync(provider, clock: clock);
        var account = await CreateSignedInAccountAsync(fixture, "frozen-history-source@example.com");
        await SeedPortfolioAsync(fixture, account.Id, 99999m, "USD", ("MSFT", 1m, 1m));
        using var scope = fixture.CreateScope();
        var frozen = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<StockLab.Api.Configuration.MarketDataProviderOptions>>().Value;
        Assert.Equal("Mock", frozen.Provider);
        var capture = scope.ServiceProvider.GetRequiredService<PortfolioPerformanceCaptureService>();
        Assert.Equal("Mock", (await capture.CaptureAsync(account.Id, default))!.Source);
        scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["MarketData:Provider"] = "TwelveData";
        clock.Now = clock.Now.AddHours(1);
        if (production)
        {
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().EnvironmentName = "Production";
            await Assert.ThrowsAsync<InvalidOperationException>(() => capture.CaptureAsync(account.Id, default));
        }
        else Assert.Equal("Mock", (await capture.CaptureAsync(account.Id, default))!.Source);
    }

    [Theory]
    [InlineData("0.00005", "0.000000000001")]
    [InlineData("0.00004999", "0")]
    public async Task Personal_history_capture_matches_twelve_place_position_rounding(string price, string expected)
    {
        var clock = new PersonalHistoryClock(DateTimeOffset.UtcNow);
        var provider = new CountingMarketDataProvider(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), clock.Now.AddMinutes(-1));
        await using var fixture = await PortfolioFixture.CreateAsync(provider, clock: clock);
        var account = await CreateSignedInAccountAsync(fixture, "rounded-history@example.com");
        await SeedPortfolioAsync(fixture, account.Id, 99999.999999999999m, "USD", ("MSFT", 0.00000001m, 0.0001m));
        using var scope = fixture.CreateScope();
        var point = await scope.ServiceProvider.GetRequiredService<PortfolioPerformanceCaptureService>().CaptureAsync(account.Id, default);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), point!.PositionsValue);
    }

    private static PortfolioValuationSnapshot HistoryPoint(Guid portfolioId, DateTime at, decimal value) => new()
    {
        Id = Guid.NewGuid(), PortfolioId = portfolioId, SlotUtc = at, RecordedAtUtc = at,
        CashBalance = value, PositionsValue = 0m, TotalValue = value, InitialCapital = 100000m,
        Currency = "CAD", Source = "Cash", PortfolioVersion = [], QuoteObservationsJson = "[]"
    };
}

internal sealed class PersonalHistoryClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
