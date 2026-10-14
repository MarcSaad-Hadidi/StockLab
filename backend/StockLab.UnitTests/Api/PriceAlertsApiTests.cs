using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Alerts;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Api;

public sealed class PriceAlertsApiTests
{
    [Theory]
    [InlineData("GET", "/api/alerts")]
    [InlineData("POST", "/api/alerts")]
    [InlineData("PUT", "/api/alerts/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/alerts/00000000-0000-0000-0000-000000000001/disable")]
    [InlineData("DELETE", "/api/alerts/00000000-0000-0000-0000-000000000001")]
    public async Task Endpoints_require_authentication(string method, string path)
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        using var response = await SendAsync(f.Client, method, path, null, "{}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("invalid_signature")]
    [InlineData("expired")]
    [InlineData("missing_sub")]
    [InlineData("invalid_sub")]
    public async Task Invalid_tokens_cannot_read_or_modify_alerts(string scenario)
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        var token = scenario switch
        {
            "malformed" => "not-a-jwt",
            "invalid_signature" => PriceAlertsFixture.SignedToken(f.UserA.ToString(), signingKey: new string('x', 40)),
            "expired" => PriceAlertsFixture.SignedToken(f.UserA.ToString(), expired: true),
            "missing_sub" => PriceAlertsFixture.SignedToken(null),
            _ => PriceAlertsFixture.SignedToken("not-a-guid")
        };
        var id = Guid.NewGuid();
        foreach (var (method, path, body) in new[]
        {
            ("GET", "/api/alerts", "{}"),
            ("POST", "/api/alerts", """{"symbol":"AAPL","condition":"Above","targetPrice":250}"""),
            ("PUT", $"/api/alerts/{id}", """{"condition":"Below","targetPrice":180}"""),
            ("POST", $"/api/alerts/{id}/disable", "{}"),
            ("DELETE", $"/api/alerts/{id}", "{}")
        })
        {
            using var response = await SendAsync(f.Client, method, path, token, body);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("unauthorized", await ErrorCodeAsync(response));
        }
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.PriceAlerts.ToArrayAsync());
    }

    [Fact]
    public async Task New_user_receives_an_empty_array()
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        Assert.Empty(await GetAlertsAsync(f, f.TokenA));
    }

    [Theory]
    [InlineData("Above", "Above")]
    [InlineData("Below", "Below")]
    [InlineData("  aBoVe  ", "Above")]
    [InlineData("below", "Below")]
    public async Task Create_normalizes_and_persists_an_active_USD_alert(string condition, string expected)
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        using var response = await SendAsync(f.Client, "POST", $"/api/alerts?userId={f.UserB}", f.TokenA,
            JsonSerializer.Serialize(new { symbol = "  aapl  ", condition, targetPrice = 250.12345m }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/alerts", response.Headers.Location?.OriginalString);
        var alert = await BodyAsync(response);
        AssertPublicFields(alert);
        Assert.Equal("AAPL", alert.GetProperty("symbol").GetString());
        Assert.Equal("USD", alert.GetProperty("currency").GetString());
        Assert.Equal(expected, alert.GetProperty("condition").GetString());
        Assert.Equal(250.1235m, alert.GetProperty("targetPrice").GetDecimal());
        Assert.Equal("Active", alert.GetProperty("status").GetString());
        Assert.Null(alert.GetProperty("triggeredPrice").GetString());
        Assert.Null(alert.GetProperty("triggeredAtUtc").GetString());
        Assert.Equal(f.Clock.Now.UtcDateTime, alert.GetProperty("createdAtUtc").GetDateTime());
        Assert.Equal(f.Clock.Now.UtcDateTime, alert.GetProperty("updatedAtUtc").GetDateTime());
        Assert.EndsWith("Z", alert.GetProperty("createdAtUtc").GetString());
        Assert.EndsWith("Z", alert.GetProperty("updatedAtUtc").GetString());
        await using var db = f.CreateDbContext();
        var stored = Assert.Single(await db.PriceAlerts.AsNoTracking().ToArrayAsync());
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(f.UserA, stored.UserId);
        Assert.Equal("AAPL", stored.Symbol);
        Assert.Equal(expected, stored.Condition);
        Assert.Equal(250.1235m, stored.TargetPrice);
        Assert.Equal("Active", stored.Status);
        Assert.Null(stored.TriggeredPrice);
        Assert.Null(stored.TriggeredAtUtc);
        Assert.Empty(await GetAlertsAsync(f, f.TokenB));
    }

    public static IEnumerable<object[]> InvalidTerms => new[]
    {
        "{}", "null", "{", """{"condition":null,"targetPrice":250}""",
        """{"condition":"","targetPrice":250}""", """{"condition":"Up","targetPrice":250}""",
        """{"condition":"Down","targetPrice":250}""", """{"condition":"Greater","targetPrice":250}""",
        """{"condition":"Above"}""", """{"condition":"Above","targetPrice":0}""",
        """{"condition":"Above","targetPrice":-1}""", """{"condition":"Above","targetPrice":0.000049}""",
        """{"condition":"Above","targetPrice":1000000000000000}""",
        """{"condition":"Above","targetPrice":999999999999999.99995}""",
        """{"condition":"Above","targetPrice":1e50}""", """{"condition":"Above","targetPrice":null}"""
    }.Select(payload => new object[] { payload });

    [Theory]
    [MemberData(nameof(InvalidTerms))]
    public async Task Create_and_update_reject_invalid_terms_before_writing(string payload)
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        var id = await CreateAlertAsync(f, f.TokenA);
        var createPayload = payload.StartsWith('{') && payload.EndsWith('}')
            ? payload.Insert(1, "\"symbol\":\"AAPL\",") : payload;
        foreach (var (method, path, body) in new[]
        {
            ("POST", "/api/alerts", createPayload), ("PUT", $"/api/alerts/{id}", payload)
        })
        {
            using var response = await SendAsync(f.Client, method, path, f.TokenA, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_error", await ErrorCodeAsync(response));
        }
        await using var db = f.CreateDbContext();
        var stored = Assert.Single(await db.PriceAlerts.ToArrayAsync());
        Assert.Equal("Above", stored.Condition);
        Assert.Equal(250m, stored.TargetPrice);
        Assert.Equal(stored.CreatedAtUtc, stored.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567")]
    public async Task Create_rejects_missing_blank_or_long_symbols(string? symbol)
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        using var response = await SendAsync(f.Client, "POST", "/api/alerts", f.TokenA,
            JsonSerializer.Serialize(new { symbol, condition = "Above", targetPrice = 250m }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_error", await ErrorCodeAsync(response));
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.PriceAlerts.ToArrayAsync());
    }

    [Theory]
    [InlineData("userId", "\"00000000-0000-0000-0000-000000000001\"")]
    [InlineData("status", "\"Triggered\"")]
    [InlineData("triggeredPrice", "123")]
    [InlineData("triggeredAtUtc", "\"2026-10-04T00:00:00Z\"")]
    [InlineData("createdAtUtc", "\"2026-10-04T00:00:00Z\"")]
    [InlineData("version", "\"AQ==\"")]
    [InlineData("currency", "\"CAD\"")]
    public async Task Bodies_reject_server_controlled_fields(string field, string value)
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        var id = await CreateAlertAsync(f, f.TokenA);
        foreach (var (method, path, symbol) in new[]
        {
            ("POST", "/api/alerts", "\"symbol\":\"AAPL\","), ("PUT", $"/api/alerts/{id}", "")
        })
        {
            using var response = await SendAsync(f.Client, method, path, f.TokenA,
                $"{{{symbol}\"condition\":\"Below\",\"targetPrice\":180,\"{field}\":{value}}}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_error", await ErrorCodeAsync(response));
        }
        await using var db = f.CreateDbContext();
        var stored = Assert.Single(await db.PriceAlerts.ToArrayAsync());
        Assert.Equal(f.UserA, stored.UserId);
        Assert.Equal("Active", stored.Status);
        Assert.Equal(250m, stored.TargetPrice);
    }

    [Theory]
    [InlineData("0.00005", "0.0001")]
    [InlineData("999999999999999.9999", "999999999999999.9999")]
    public async Task Storage_price_boundaries_are_supported(string input, string expected)
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        using var response = await SendAsync(f.Client, "POST", "/api/alerts", f.TokenA,
            $"{{\"symbol\":\"{new string('x', 32)}\",\"condition\":\"Above\",\"targetPrice\":{input}}}");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), body.GetProperty("targetPrice").GetDecimal());
        Assert.Equal(new string('X', 32), body.GetProperty("symbol").GetString());
    }

    [Fact]
    public async Task Multiple_alerts_per_symbol_are_allowed_and_lists_are_isolated_and_newest_first()
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        var first = await CreateAlertAsync(f, f.TokenA);
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        var second = await CreateAlertAsync(f, f.TokenA, "AAPL", "Below", 180m);
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        var third = await CreateAlertAsync(f, f.TokenA, "AAPL", "Above", 300m);
        var other = await CreateAlertAsync(f, f.TokenB, "MSFT", "Below", 300m);
        Assert.Equal(new[] { third, second, first }, (await GetAlertsAsync(f, f.TokenA)).Select(a => a.GetProperty("id").GetGuid()));
        Assert.Equal(other, Assert.Single(await GetAlertsAsync(f, f.TokenB)).GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_preserves_symbol_status_creation_and_trigger_fields(bool disabled)
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        var id = await CreateAlertAsync(f, f.TokenA);
        if (disabled)
        {
            using var disable = await SendAsync(f.Client, "POST", $"/api/alerts/{id}/disable", f.TokenA);
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        }
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        using var response = await SendAsync(f.Client, "PUT", $"/api/alerts/{id}", f.TokenA,
            """{"condition":"below","targetPrice":180.12345}""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var alert = await BodyAsync(response);
        Assert.Equal("Below", alert.GetProperty("condition").GetString());
        Assert.Equal(180.1235m, alert.GetProperty("targetPrice").GetDecimal());
        Assert.Equal("AAPL", alert.GetProperty("symbol").GetString());
        Assert.Equal(disabled ? "Disabled" : "Active", alert.GetProperty("status").GetString());
        Assert.True(alert.GetProperty("updatedAtUtc").GetDateTime() > alert.GetProperty("createdAtUtc").GetDateTime());
        Assert.Equal(JsonValueKind.Null, alert.GetProperty("triggeredPrice").ValueKind);
        Assert.Equal(JsonValueKind.Null, alert.GetProperty("triggeredAtUtc").ValueKind);
        await using var db = f.CreateDbContext();
        var stored = await db.PriceAlerts.SingleAsync();
        Assert.Equal(180.1235m, stored.TargetPrice);
        Assert.Equal(f.Clock.Now.UtcDateTime, stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task Update_rejects_a_symbol_change()
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        var id = await CreateAlertAsync(f, f.TokenA);
        using var response = await SendAsync(f.Client, "PUT", $"/api/alerts/{id}", f.TokenA,
            """{"symbol":"MSFT","condition":"Below","targetPrice":180}""");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("AAPL", Assert.Single(await GetAlertsAsync(f, f.TokenA)).GetProperty("symbol").GetString());
    }

    [Fact]
    public async Task Disable_is_idempotent_and_keeps_trigger_fields_empty()
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        var id = await CreateAlertAsync(f, f.TokenA);
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        using var first = await SendAsync(f.Client, "POST", $"/api/alerts/{id}/disable", f.TokenA);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var disabled = await BodyAsync(first);
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        using var second = await SendAsync(f.Client, "POST", $"/api/alerts/{id}/disable", f.TokenA);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var unchanged = await BodyAsync(second);
        Assert.Equal(disabled.GetProperty("updatedAtUtc").GetString(), unchanged.GetProperty("updatedAtUtc").GetString());
        Assert.Equal("Disabled", unchanged.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("triggeredPrice").ValueKind);
        Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("triggeredAtUtc").ValueKind);
    }

    [Theory]
    [InlineData("PUT", "")]
    [InlineData("POST", "/disable")]
    [InlineData("DELETE", "")]
    public async Task Mutations_hide_missing_and_other_users_alerts(string method, string suffix)
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        var otherId = await CreateAlertAsync(f, f.TokenB);
        foreach (var id in new[] { otherId, Guid.NewGuid() })
        {
            using var response = await SendAsync(f.Client, method, $"/api/alerts/{id}{suffix}", f.TokenA,
                """{"condition":"Below","targetPrice":180}""");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("alert_not_found", await ErrorCodeAsync(response));
        }
        var other = Assert.Single(await GetAlertsAsync(f, f.TokenB));
        Assert.Equal("Active", other.GetProperty("status").GetString());
        Assert.Equal(250m, other.GetProperty("targetPrice").GetDecimal());
    }

    [Fact]
    public async Task Delete_removes_only_the_owned_alert()
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        var id = await CreateAlertAsync(f, f.TokenA);
        var other = await CreateAlertAsync(f, f.TokenB);
        using var response = await SendAsync(f.Client, "DELETE", $"/api/alerts/{id}", f.TokenA);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.Empty(await GetAlertsAsync(f, f.TokenA));
        Assert.Equal(other, Assert.Single(await GetAlertsAsync(f, f.TokenB)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Triggered_alerts_are_readable_and_deletable_but_cannot_be_modified_or_disabled()
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        var id = await CreateAlertAsync(f, f.TokenA);
        await using (var db = f.CreateDbContext())
        {
            var alert = await db.PriceAlerts.SingleAsync();
            alert.Status = "Triggered";
            alert.TriggeredPrice = 251m;
            alert.TriggeredAtUtc = f.Clock.Now.UtcDateTime;
            await db.SaveChangesAsync();
        }
        foreach (var (method, suffix) in new[] { ("PUT", ""), ("POST", "/disable") })
        {
            using var response = await SendAsync(f.Client, method, $"/api/alerts/{id}{suffix}", f.TokenA,
                """{"condition":"Below","targetPrice":180}""");
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("alert_already_triggered", await ErrorCodeAsync(response));
        }
        var triggered = Assert.Single(await GetAlertsAsync(f, f.TokenA));
        Assert.Equal("Triggered", triggered.GetProperty("status").GetString());
        Assert.Equal(251m, triggered.GetProperty("triggeredPrice").GetDecimal());
        Assert.EndsWith("Z", triggered.GetProperty("triggeredAtUtc").GetString());
        using var delete = await SendAsync(f.Client, "DELETE", $"/api/alerts/{id}", f.TokenA);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(await GetAlertsAsync(f, f.TokenA));
    }

    [Theory]
    [InlineData("PUT", "")]
    [InlineData("POST", "/disable")]
    [InlineData("DELETE", "")]
    public async Task Rowversion_conflicts_return_409_without_partial_changes(string method, string suffix)
    {
        await using var f = await PriceAlertsFixture.CreateAsync(new ChangeAlertVersion());
        var id = await CreateAlertAsync(f, f.TokenA);
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        using var response = await SendAsync(f.Client, method, $"/api/alerts/{id}{suffix}", f.TokenA,
            """{"condition":"Below","targetPrice":180}""");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("alert_update_conflict", await ErrorCodeAsync(response));
        var unchanged = Assert.Single(await GetAlertsAsync(f, f.TokenA));
        Assert.Equal("Active", unchanged.GetProperty("status").GetString());
        Assert.Equal(250m, unchanged.GetProperty("targetPrice").GetDecimal());
        Assert.Equal(unchanged.GetProperty("createdAtUtc").GetString(), unchanged.GetProperty("updatedAtUtc").GetString());
    }

    [Fact]
    public async Task OpenApi_documents_all_routes_bearer_auth_and_public_contracts()
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        using var response = await f.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await BodyAsync(response);
        foreach (var (path, method, statuses) in new[]
        {
            ("/api/alerts", "get", new[] { "200", "401", "500" }),
            ("/api/alerts", "post", new[] { "201", "400", "401", "500" }),
            ("/api/alerts/{id}", "put", new[] { "200", "400", "401", "404", "409", "500" }),
            ("/api/alerts/{id}/disable", "post", new[] { "200", "401", "404", "409", "500" }),
            ("/api/alerts/{id}", "delete", new[] { "204", "401", "404", "409", "500" })
        })
        {
            var operation = document.GetProperty("paths").GetProperty(path).GetProperty(method);
            foreach (var status in statuses) Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
            Assert.Contains(operation.GetProperty("security").EnumerateArray(), r => r.TryGetProperty("Bearer", out _));
        }
        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.Equal(new[] { "symbol", "condition", "targetPrice" }, schemas.GetProperty("CreatePriceAlertRequest")
            .GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "condition", "targetPrice" }, schemas.GetProperty("UpdatePriceAlertRequest")
            .GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Equal(PublicFields, schemas.GetProperty("PriceAlertResponse").GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Listing_does_not_track_entities_or_flush_pending_changes()
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        await CreateAlertAsync(f, f.TokenA);
        await using var db = f.CreateDbContext();
        var user = await db.Users.SingleAsync(u => u.Id == f.UserA);
        user.DisplayName = "Unsaved";
        var service = new PriceAlertService(db, f.Clock);
        Assert.Single(await service.GetAllAsync(f.UserA, CancellationToken.None));
        Assert.Empty(db.ChangeTracker.Entries<PriceAlert>());
        await using var verifier = f.CreateDbContext();
        Assert.Equal("Price alert test", (await verifier.Users.SingleAsync(u => u.Id == f.UserA)).DisplayName);
    }

    [Fact]
    public async Task Clock_corrections_preserve_the_timestamp_invariant()
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        var id = await CreateAlertAsync(f, f.TokenA);
        f.Clock.Now = f.Clock.Now.AddMinutes(-1);
        using var update = await SendAsync(f.Client, "PUT", $"/api/alerts/{id}", f.TokenA,
            """{"condition":"Below","targetPrice":180}""");
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var disable = await SendAsync(f.Client, "POST", $"/api/alerts/{id}/disable", f.TokenA);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        var alert = await BodyAsync(disable);
        Assert.True(alert.GetProperty("updatedAtUtc").GetDateTime() >= alert.GetProperty("createdAtUtc").GetDateTime());
    }

    [Fact]
    public async Task Login_and_alert_lifecycle_survive_application_restart()
    {
        await using var f = await PriceAlertsFixture.CreateAsync();
        await VerifyPersistentLifecycleAsync(f);
    }

    internal static async Task VerifyPersistentLifecycleAsync(PriceAlertsFixture f)
    {
        using var login = await SendAsync(f.Client, "POST", "/api/auth/login", null,
            JsonSerializer.Serialize(new { email = f.EmailA, password = PriceAlertsFixture.Password }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await BodyAsync(login)).GetProperty("accessToken").GetString()!;
        var id = await CreateAlertAsync(f, token, "  aapl  ");
        Assert.Single(await GetAlertsAsync(f, token));
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        using var update = await SendAsync(f.Client, "PUT", $"/api/alerts/{id}", token,
            """{"condition":"Below","targetPrice":180.12345}""");
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        using var disable = await SendAsync(f.Client, "POST", $"/api/alerts/{id}/disable", token);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        await f.RestartAsync();
        var persisted = Assert.Single(await GetAlertsAsync(f, token));
        Assert.Equal("Disabled", persisted.GetProperty("status").GetString());
        Assert.Equal(180.1235m, persisted.GetProperty("targetPrice").GetDecimal());
        await using (var db = f.CreateDbContext())
        {
            var stored = await db.PriceAlerts.AsNoTracking().SingleAsync(a => a.Id == id);
            Assert.Equal(f.UserA, stored.UserId);
            Assert.Equal("AAPL", stored.Symbol);
            Assert.Equal("USD", stored.Currency);
            Assert.Equal("Below", stored.Condition);
            Assert.Equal("Disabled", stored.Status);
            Assert.Equal(180.1235m, stored.TargetPrice);
            Assert.Null(stored.TriggeredPrice);
            Assert.Null(stored.TriggeredAtUtc);
            Assert.True(stored.UpdatedAtUtc > stored.CreatedAtUtc);
        }
        using var delete = await SendAsync(f.Client, "DELETE", $"/api/alerts/{id}", token);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(await GetAlertsAsync(f, token));
    }

    private static readonly string[] PublicFields = ["id", "symbol", "currency", "condition", "targetPrice", "status",
        "triggeredPrice", "triggeredAtUtc", "createdAtUtc", "updatedAtUtc"];
    private static void AssertPublicFields(JsonElement alert) => Assert.Equal(PublicFields, alert.EnumerateObject().Select(p => p.Name));

    internal static async Task<Guid> CreateAlertAsync(PriceAlertsFixture f, string token, string symbol = "AAPL", string condition = "Above", decimal targetPrice = 250m)
    {
        using var response = await SendAsync(f.Client, "POST", "/api/alerts", token, JsonSerializer.Serialize(new { symbol, condition, targetPrice }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    internal static async Task<JsonElement[]> GetAlertsAsync(PriceAlertsFixture f, string token)
    {
        using var response = await SendAsync(f.Client, "GET", "/api/alerts", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).EnumerateArray().Select(a => a.Clone()).ToArray();
    }

    internal static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, string? token, string? payload = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null && method is "POST" or "PUT") request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    internal static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }
    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response) => (await BodyAsync(response)).GetProperty("error").GetString()!;

    internal sealed class ChangeAlertVersion : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            foreach (var entry in db.ChangeTracker.Entries<PriceAlert>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                // A separate write after the service's lookup makes EF's original token stale.
                if (db.Database.IsSqlServer())
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PriceAlerts SET TargetPrice = TargetPrice WHERE Id = {entry.Entity.Id}", cancellationToken);
                else
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PriceAlerts SET Version = {Guid.NewGuid().ToByteArray()} WHERE Id = {entry.Entity.Id}", cancellationToken);
            }
            return result;
        }
    }
}

internal sealed class PriceAlertsFixture : IAsyncDisposable
{
    private const string Issuer = "StockLab.PriceAlerts.Tests";
    private const string Audience = "StockLab.Tests";
    private const string SigningKey = "test-only-signing-key-at-least-32-bytes-long";
    internal const string Password = "price-alert-tests-only-password";
    private WebApplicationFactory<Program> application;
    private readonly DbContextOptions<StockLabDbContext> options;
    private readonly string? path;
    private readonly bool ownsDatabase;
    private readonly bool sqlServer;
    private readonly bool ownsSqlDatabase;
    private bool sqlRowsCreated;
    public HttpClient Client { get; private set; }
    public TestClock Clock { get; }
    public Guid UserA { get; } = Guid.NewGuid();
    public Guid UserB { get; } = Guid.NewGuid();
    public string EmailA => $"price-alert-test-{UserA:N}@example.com";
    public string TokenA => SignedToken(UserA.ToString());
    public string TokenB => SignedToken(UserB.ToString());

    private PriceAlertsFixture(DbContextOptions<StockLabDbContext> options, TestClock clock, string? path, bool ownsDatabase, bool sqlServer, bool ownsSqlDatabase)
    {
        this.options = options;
        this.path = path;
        this.ownsDatabase = ownsDatabase;
        this.sqlServer = sqlServer;
        this.ownsSqlDatabase = ownsSqlDatabase;
        Clock = clock;
        application = CreateApplication();
        Client = application.CreateClient();
    }

    private WebApplicationFactory<Program> CreateApplication() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StockLab"] = "Server=localhost;Database=PriceAlertsTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true",
                ["Jwt:Issuer"] = Issuer, ["Jwt:Audience"] = Audience, ["Jwt:SigningKey"] = SigningKey,
                ["MarketData:Provider"] = "Mock"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<StockLabDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<StockLabDbContext>();
                services.AddSingleton(options);
                if (sqlServer) services.AddScoped<StockLabDbContext>();
                else services.AddScoped<StockLabDbContext, SqliteAlertsDbContext>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        });

    internal async Task RestartAsync()
    {
        Client.Dispose();
        await application.DisposeAsync();
        application = CreateApplication();
        Client = application.CreateClient();
    }

    public StockLabDbContext CreateDbContext() => sqlServer ? new StockLabDbContext(options) : new SqliteAlertsDbContext(options);

    public static async Task<PriceAlertsFixture> CreateAsync(SaveChangesInterceptor? interceptor = null, string? databasePath = null, string? sqlConnection = null, bool localDb = false)
    {
        if (localDb)
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1"
                || sqlConnection is not null || databasePath is not null)
                throw new InvalidOperationException("Disposable LocalDB requires explicit opt-in and no external database.");
            sqlConnection = $"Server=(localdb)\\MSSQLLocalDB;Database=StockLabPriceAlertsTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true";
        }
        var path = sqlConnection is null ? databasePath ?? Path.Combine(Path.GetTempPath(), $"stocklab-alerts-{Guid.NewGuid():N}.db") : null;
        var builder = new DbContextOptionsBuilder<StockLabDbContext>();
        if (sqlConnection is null) builder.UseSqlite($"Data Source={path};Pooling=False");
        else builder.UseSqlServer(sqlConnection);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        var f = new PriceAlertsFixture(builder.Options, new TestClock(), path, databasePath is null, sqlConnection is not null, localDb);
        try
        {
            await using var db = f.CreateDbContext();
            // The opt-in SQL smoke uses the existing schema, with no migrations or DDL.
            if (sqlConnection is null) await db.Database.EnsureCreatedAsync();
            else if (localDb) await db.Database.MigrateAsync();
            foreach (var id in new[] { f.UserA, f.UserB })
            {
                var email = $"price-alert-test-{id:N}@example.com";
                var user = new User
                {
                    Id = id, DisplayName = "Price alert test", Email = email, NormalizedEmail = email.ToUpperInvariant(),
                    CreatedAtUtc = f.Clock.Now.UtcDateTime, UpdatedAtUtc = f.Clock.Now.UtcDateTime
                };
                user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
                user.Portfolio = new Portfolio { Id = Guid.NewGuid(), UserId = id, CreatedAtUtc = f.Clock.Now.UtcDateTime };
                db.Users.Add(user);
            }
            await db.SaveChangesAsync();
            f.sqlRowsCreated = sqlConnection is not null;
            return f;
        }
        catch { await f.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await application.DisposeAsync();
        if (ownsSqlDatabase)
        {
            await using var db = CreateDbContext();
            // Only the generated database owned by this fixture can be dropped.
            await db.Database.EnsureDeletedAsync();
        }
        else if (sqlServer && sqlRowsCreated)
        {
            await using var db = CreateDbContext();
            // Only delete rows created and owned by this fixture.
            await db.PriceAlerts.Where(a => a.UserId == UserA || a.UserId == UserB).ExecuteDeleteAsync();
            await db.Portfolios.Where(p => p.UserId == UserA || p.UserId == UserB).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == UserA || u.Id == UserB).ExecuteDeleteAsync();
        }
        else if (ownsDatabase && path is not null) File.Delete(path);
    }

    internal static string SignedToken(string? subject, bool expired = false, string signingKey = SigningKey)
    {
        var now = DateTime.UtcNow;
        var claims = subject is null ? Array.Empty<System.Security.Claims.Claim>()
            : new[] { new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, subject) };
        var token = new JwtSecurityToken(Issuer, Audience, claims, now.AddHours(-2), expired ? now.AddHours(-1) : now.AddMinutes(30),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    internal sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class SqliteAlertsDbContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().Property(u => u.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(p => p.Version).ValueGeneratedNever();
            modelBuilder.Entity<PriceAlert>().Property(a => a.Version).ValueGeneratedNever();
        }
    }
}
