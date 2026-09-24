using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Api;

public sealed class UserProfileApiTests
{
    private const string TestIssuer = "StockLab.Api.Tests";
    private const string TestAudience = "StockLab.Tests";
    private const string TestSigningKey = "test-only-signing-key-at-least-32-bytes-long";

    public static IEnumerable<object[]> InvalidProfileRequests
    {
        get
        {
            yield return ["{}", "displayName"];
            yield return [JsonSerializer.Serialize(new { displayName = "   ", email = "person@example.com" }), "displayName"];
            yield return [JsonSerializer.Serialize(new { displayName = new string('x', 101), email = "person@example.com" }), "displayName"];
            yield return [JsonSerializer.Serialize(new { displayName = "Person" }), "email"];
            yield return [JsonSerializer.Serialize(new { displayName = "Person", email = "" }), "email"];
            yield return [JsonSerializer.Serialize(new { displayName = "Person", email = "not-an-email" }), "email"];
            yield return [JsonSerializer.Serialize(new { displayName = "Person", email = new string('x', 255) }), "email"];
        }
    }

    [Fact]
    public async Task Get_and_put_profile_require_authentication()
    {
        await using var fixture = await UserProfileFixture.CreateAsync();

        using var getResponse = await fixture.Client.GetAsync("/api/profile");
        using var putResponse = await fixture.Client.PutAsync("/api/profile", Json("""{"displayName":"Person","email":"person@example.com"}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, putResponse.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(getResponse));
        Assert.Equal("unauthorized", await ErrorCodeAsync(putResponse));
    }

    [Fact]
    public async Task Get_profile_uses_sub_to_isolate_users_and_returns_only_public_profile_fields()
    {
        await using var fixture = await UserProfileFixture.CreateAsync();
        var accountA = await CreateSignedInAccountAsync(fixture, "User A", "a@example.com");
        var accountB = await CreateSignedInAccountAsync(fixture, "User B", "b@example.com");

        using var responseA = await GetWithTokenAsync(fixture.Client, accountA.Token);
        using var responseB = await GetWithTokenAsync(fixture.Client, accountB.Token);

        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        var responseTextA = await responseA.Content.ReadAsStringAsync();
        var responseTextB = await responseB.Content.ReadAsStringAsync();
        using var bodyA = JsonDocument.Parse(responseTextA);
        using var bodyB = JsonDocument.Parse(responseTextB);
        Assert.Equal(accountA.Id, bodyA.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("User A", bodyA.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("a@example.com", bodyA.RootElement.GetProperty("email").GetString());
        Assert.Equal(accountB.Id, bodyB.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("User B", bodyB.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("b@example.com", bodyB.RootElement.GetProperty("email").GetString());
        Assert.Equal(
            new[] { "id", "displayName", "email", "createdAtUtc", "updatedAtUtc" },
            bodyA.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.DoesNotContain("password", responseTextA, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", responseTextA, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("normalizedEmail", responseTextA, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("version", responseTextA, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("portfolio", responseTextA, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_profile_returns_not_found_when_the_sub_user_no_longer_exists()
    {
        await using var fixture = await UserProfileFixture.CreateAsync();
        var token = CreateSignedToken(Guid.NewGuid().ToString("D"), fixture.ExpectedUtcNow);

        using var response = await GetWithTokenAsync(fixture.Client, token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("profile_not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Put_profile_returns_not_found_when_the_sub_user_no_longer_exists()
    {
        await using var fixture = await UserProfileFixture.CreateAsync();
        var token = CreateSignedToken(Guid.NewGuid().ToString("D"), fixture.ExpectedUtcNow);

        using var response = await PutWithTokenAsync(fixture.Client, token,
            """{"displayName":"Person","email":"person@example.com"}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("profile_not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Invalid_signature_and_unparseable_sub_return_controlled_unauthorized_responses()
    {
        await using var fixture = await UserProfileFixture.CreateAsync();
        var invalidTokens = new[]
        {
            "not-a-jwt",
            CreateSignedToken(Guid.NewGuid().ToString("D"), fixture.ExpectedUtcNow, "another-test-only-signing-key-at-least-32-bytes"),
            CreateSignedToken("not-a-guid", fixture.ExpectedUtcNow)
        };

        foreach (var token in invalidTokens)
        {
            using var response = await GetWithTokenAsync(fixture.Client, token);
            using var updateResponse = await PutWithTokenAsync(fixture.Client, token,
                """{"displayName":"Person","email":"person@example.com"}""");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("unauthorized", await ErrorCodeAsync(response));
            Assert.Equal(HttpStatusCode.Unauthorized, updateResponse.StatusCode);
            Assert.Equal("unauthorized", await ErrorCodeAsync(updateResponse));
        }
    }

    [Theory]
    [MemberData(nameof(InvalidProfileRequests))]
    public async Task Invalid_profile_update_uses_shared_validation_response(string payload, string field)
    {
        await using var fixture = await UserProfileFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "Person", "person@example.com");

        using var response = await PutWithTokenAsync(fixture.Client, account.Token, payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(responseText);
        Assert.Equal("validation_error", body.RootElement.GetProperty("error").GetString());
        Assert.Equal("The request contains invalid data.", body.RootElement.GetProperty("message").GetString());
        Assert.Contains(field, body.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Put_profile_trims_fields_and_accepts_the_same_email_after_normalization()
    {
        await using var fixture = await UserProfileFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "Ghaith", "Ghaith@Test.com");

        using var response = await PutWithTokenAsync(fixture.Client, account.Token,
            """{"displayName":"  New Name  ","email":"  ghaith@test.com  "}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("New Name", body.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("ghaith@test.com", body.RootElement.GetProperty("email").GetString());
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var user = await context.Users.AsNoTracking().SingleAsync();
        Assert.Equal("GHAITH@TEST.COM", user.NormalizedEmail);
        Assert.Equal(1, await context.Users.CountAsync());
    }

    [Fact]
    public async Task Put_profile_updates_email_and_timestamps_without_changing_protected_fields()
    {
        await using var fixture = await UserProfileFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "Ghaith", "old@example.com");
        using var initialScope = fixture.CreateScope();
        var initialContext = initialScope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var originalPasswordHash = await initialContext.Users.AsNoTracking()
            .Where(user => user.Id == account.Id).Select(user => user.PasswordHash).SingleAsync();
        var originalVersion = await initialContext.Users.AsNoTracking().Where(user => user.Id == account.Id).Select(user => user.Version).SingleAsync();
        var updateTime = fixture.ExpectedUtcNow.AddMinutes(1);
        fixture.Clock.SetUtcNow(updateTime);

        using (var response = await PutWithTokenAsync(fixture.Client, account.Token,
                   """{"displayName":"  New Name  ","email":"  New@Test.com  "}"""))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var responseText = await response.Content.ReadAsStringAsync();
            using var body = JsonDocument.Parse(responseText);
            Assert.Equal(account.Id, body.RootElement.GetProperty("id").GetGuid());
            Assert.Equal("New Name", body.RootElement.GetProperty("displayName").GetString());
            Assert.Equal("New@Test.com", body.RootElement.GetProperty("email").GetString());
            Assert.Equal(fixture.ExpectedUtcNow.UtcDateTime,
                body.RootElement.GetProperty("createdAtUtc").GetDateTime());
            Assert.Equal(updateTime.UtcDateTime, body.RootElement.GetProperty("updatedAtUtc").GetDateTime());
            Assert.Equal(
                new[] { "id", "displayName", "email", "createdAtUtc", "updatedAtUtc" },
                body.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.DoesNotContain("password", responseText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", responseText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("version", responseText, StringComparison.OrdinalIgnoreCase);
        }

        using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            var user = await context.Users.AsNoTracking().Include(item => item.Portfolio).SingleAsync();
            Assert.Equal("NEW@TEST.COM", user.NormalizedEmail);
            Assert.Equal(fixture.ExpectedUtcNow.UtcDateTime, user.CreatedAtUtc);
            Assert.Equal(updateTime.UtcDateTime, user.UpdatedAtUtc);
            Assert.Equal(originalPasswordHash, user.PasswordHash);
            Assert.Equal(originalVersion, user.Version);
            Assert.NotNull(user.Portfolio);
            Assert.Equal(100_000m, user.Portfolio.InitialCapital);
            Assert.Equal(100_000m, user.Portfolio.CashBalance);
            Assert.Equal("USD", user.Portfolio.Currency);
        }

        using var newEmailLogin = await LoginAsync(fixture.Client, "new@test.com");
        using var oldEmailLogin = await LoginAsync(fixture.Client, "old@example.com");
        Assert.Equal(HttpStatusCode.OK, newEmailLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldEmailLogin.StatusCode);
    }

    [Fact]
    public async Task Put_profile_rejects_an_email_used_by_another_user_without_modifying_either_user()
    {
        await using var fixture = await UserProfileFixture.CreateAsync();
        var accountA = await CreateSignedInAccountAsync(fixture, "User A", "a@example.com");
        var accountB = await CreateSignedInAccountAsync(fixture, "User B", "b@example.com");

        using var response = await PutWithTokenAsync(fixture.Client, accountA.Token,
            """{"displayName":"Changed A","email":" B@EXAMPLE.COM "}""");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("email_already_registered", await ErrorCodeAsync(response));
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var users = await context.Users.AsNoTracking().OrderBy(user => user.Id).ToArrayAsync();
        Assert.Equal("a@example.com", users.Single(user => user.Id == accountA.Id).Email);
        Assert.Equal("b@example.com", users.Single(user => user.Id == accountB.Id).Email);
        Assert.Equal("User A", users.Single(user => user.Id == accountA.Id).DisplayName);
    }

    [Fact]
    public async Task Concurrent_email_unique_constraint_violation_returns_controlled_conflict()
    {
        var interceptor = new InsertDuplicateEmailBeforeProfileSave();
        await using var fixture = await UserProfileFixture.CreateAsync(interceptor);
        var account = await CreateSignedInAccountAsync(fixture, "Original", "original@example.com");

        using var response = await PutWithTokenAsync(fixture.Client, account.Token,
            """{"displayName":"Changed","email":"race@example.com"}""");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("email_already_registered", await ErrorCodeAsync(response));
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Sqlite", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("race@example.com", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, interceptor.InsertedCount);
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var original = await context.Users.AsNoTracking().SingleAsync(user => user.Id == account.Id);
        Assert.Equal("original@example.com", original.Email);
        Assert.Equal("Original", original.DisplayName);
        Assert.Equal("RACE@EXAMPLE.COM",
            await context.Users.AsNoTracking().Where(user => user.Id != account.Id)
                .Select(user => user.NormalizedEmail).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_profile_update_returns_controlled_conflict()
    {
        await using var fixture = await UserProfileFixture.CreateAsync(new RejectProfileUpdateWithConcurrencyConflict());
        var account = await CreateSignedInAccountAsync(fixture, "Original", "original@example.com");

        using var response = await PutWithTokenAsync(fixture.Client, account.Token,
            """{"displayName":"Changed","email":"changed@example.com"}""");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("profile_update_conflict", await ErrorCodeAsync(response));
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var user = await context.Users.AsNoTracking().SingleAsync();
        Assert.Equal("Original", user.DisplayName);
        Assert.Equal("original@example.com", user.Email);
    }

    [Fact]
    public async Task OpenApi_describes_profile_auth_validation_and_conflict_contracts()
    {
        await using var fixture = await UserProfileFixture.CreateAsync();

        using var response = await fixture.Client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var getOperation = paths.GetProperty("/api/profile").GetProperty("get");
        var putOperation = paths.GetProperty("/api/profile").GetProperty("put");
        AssertStatuses(getOperation, "200", "401", "404", "500");
        AssertStatuses(putOperation, "200", "400", "401", "404", "409", "500");
        AssertBearerSecurity(getOperation);
        AssertBearerSecurity(putOperation);

        var responseSchema = ResolveResponseSchema(document.RootElement, getOperation, "200");
        Assert.Equal(
            new[] { "id", "displayName", "email", "createdAtUtc", "updatedAtUtc" },
            responseSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        var requestSchemaReference = putOperation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var requestSchemaName = requestSchemaReference.Split('/').Last();
        Assert.Equal(
            new[] { "displayName", "email" },
            document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(requestSchemaName)
                .GetProperty("properties").EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    private static void AssertStatuses(JsonElement operation, params string[] expected)
    {
        var responses = operation.GetProperty("responses");
        foreach (var status in expected)
            Assert.True(responses.TryGetProperty(status, out _), $"Missing OpenAPI response {status}.");
    }

    private static void AssertBearerSecurity(JsonElement operation)
    {
        var security = operation.GetProperty("security");
        Assert.Contains(security.EnumerateArray(), requirement =>
            requirement.EnumerateObject().Any(scheme => scheme.Name == "Bearer"));
    }

    private static JsonElement ResolveResponseSchema(JsonElement document, JsonElement operation, string status)
    {
        var reference = operation.GetProperty("responses").GetProperty(status).GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var schemaName = reference.Split('/').Last();
        return document.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
    }

    private static async Task<(Guid Id, string Token)> CreateSignedInAccountAsync(
        UserProfileFixture fixture, string displayName, string email)
    {
        using var registerResponse = await fixture.Client.PostAsync("/api/auth/register", Json(JsonSerializer.Serialize(new
        {
            displayName,
            email,
            password = "correct horse battery staple"
        })));
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        using var registration = JsonDocument.Parse(await registerResponse.Content.ReadAsStringAsync());
        var id = registration.RootElement.GetProperty("id").GetGuid();

        using var loginResponse = await LoginAsync(fixture.Client, email);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        using var login = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var token = login.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return (id, token!);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email) =>
        client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new
        {
            email,
            password = "correct horse battery staple"
        })));

    private static Task<HttpResponseMessage> GetWithTokenAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PutWithTokenAsync(HttpClient client, string token, string payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/profile") { Content = Json(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static string CreateSignedToken(string subject, DateTimeOffset issuedAtUtc, string signingKey = TestSigningKey)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, subject)],
            notBefore: issuedAtUtc.AddMinutes(-1).UtcDateTime,
            expires: issuedAtUtc.AddMinutes(30).UtcDateTime,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString()!;
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    private sealed class UserProfileFixture(SqliteConnection connection, WebApplicationFactory<Program> application,
        HttpClient client, MutableTimeProvider clock, DateTimeOffset expectedUtcNow) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public MutableTimeProvider Clock { get; } = clock;
        public DateTimeOffset ExpectedUtcNow { get; } = expectedUtcNow;
        public IServiceScope CreateScope() => application.Services.CreateScope();

        public static async Task<UserProfileFixture> CreateAsync(SaveChangesInterceptor? saveChangesInterceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var optionsBuilder = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection);
            if (saveChangesInterceptor is not null)
                optionsBuilder.AddInterceptors(saveChangesInterceptor);
            var options = optionsBuilder.Options;
            var current = DateTimeOffset.UtcNow;
            var expectedUtcNow = new DateTimeOffset(current.Year, current.Month, current.Day,
                current.Hour, current.Minute, current.Second, TimeSpan.Zero);
            var clock = new MutableTimeProvider(expectedUtcNow);

            var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StockLab"] = "Server=localhost;Database=ProfileTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true",
                        ["Jwt:Issuer"] = TestIssuer,
                        ["Jwt:Audience"] = TestAudience,
                        ["Jwt:SigningKey"] = TestSigningKey,
                        ["Jwt:AccessTokenMinutes"] = "60"
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<StockLabDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<StockLabDbContext>();
                    services.AddSingleton(options);
                    services.AddScoped<StockLabDbContext, SqliteProfileDbContext>();
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(clock);
                });
            });

            try
            {
                var client = application.CreateClient();
                using var scope = application.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Database.EnsureCreatedAsync();
                return new UserProfileFixture(connection, application, client, clock, expectedUtcNow);
            }
            catch
            {
                await application.DisposeAsync();
                await connection.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class SqliteProfileDbContext(DbContextOptions<StockLabDbContext> options)
        : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().Property(user => user.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(portfolio => portfolio.Version).ValueGeneratedNever();
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long utcTicks = utcNow.UtcDateTime.Ticks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref utcTicks), TimeSpan.Zero);

        public void SetUtcNow(DateTimeOffset value) => Interlocked.Exchange(ref utcTicks, value.UtcDateTime.Ticks);
    }

    private sealed class RejectProfileUpdateWithConcurrencyConflict : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var profileUpdateIsPending = eventData.Context?.ChangeTracker.Entries<User>()
                .Any(entry => entry.State == EntityState.Modified && entry.Property(user => user.Email).IsModified) == true;
            if (profileUpdateIsPending)
                throw new DbUpdateConcurrencyException("Simulated profile update conflict.");

            return ValueTask.FromResult(result);
        }
    }

    private sealed class InsertDuplicateEmailBeforeProfileSave : SaveChangesInterceptor
    {
        private int inserted;

        public int InsertedCount => Volatile.Read(ref inserted);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is StockLabDbContext context
                && context.ChangeTracker.Entries<User>().Any(entry =>
                    entry.State == EntityState.Modified && entry.Property(user => user.Email).IsModified)
                && Interlocked.CompareExchange(ref inserted, 1, 0) == 0)
            {
                var connection = (SqliteConnection)context.Database.GetDbConnection();
                var now = context.ChangeTracker.Entries<User>()
                    .Single(entry => entry.State == EntityState.Modified).Entity.UpdatedAtUtc;
                var timestamp = now.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO "Users" ("Id", "DisplayName", "Email", "NormalizedEmail", "PasswordHash", "CreatedAtUtc", "UpdatedAtUtc", "Version")
                    VALUES ($id, 'Concurrent User', 'race@example.com', 'RACE@EXAMPLE.COM', 'test-only-hash', $createdAtUtc, $updatedAtUtc, $version)
                    """;
                command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                command.Parameters.AddWithValue("$createdAtUtc", timestamp);
                command.Parameters.AddWithValue("$updatedAtUtc", timestamp);
                command.Parameters.AddWithValue("$version", new byte[8]);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
