using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Api;

public sealed class UserLoginApiTests
{
    private const string TestIssuer = "StockLab.Api.Tests";
    private const string TestAudience = "StockLab.Tests";
    private const string TestSigningKey = "test-only-signing-key-at-least-32-bytes-long";
    private const string UnauthorizedJson = "{\"error\":\"unauthorized\",\"message\":\"Authentication is required.\"}";

    public static IEnumerable<object[]> InvalidLoginPayloads
    {
        get
        {
            yield return ["{}", "email"];
            yield return [JsonSerializer.Serialize(new { email = "not-an-email", password = "secret" }), "email"];
            yield return [JsonSerializer.Serialize(new { email = new string('x', 255), password = "secret" }), "email"];
            yield return [JsonSerializer.Serialize(new { email = "person@example.com" }), "password"];
            yield return [JsonSerializer.Serialize(new { email = "person@example.com", password = "   " }), "password"];
        }
    }

    public static IEnumerable<object[]> InvalidJwtConfigurations
    {
        get
        {
            yield return ["", TestAudience, TestSigningKey, "60"];
            yield return [TestIssuer, "", TestSigningKey, "60"];
            yield return [TestIssuer, TestAudience, "short", "60"];
            yield return [TestIssuer, TestAudience, TestSigningKey, "0"];
        }
    }

    public static IEnumerable<object[]> InvalidLoginRateLimitConfigurations
    {
        get
        {
            yield return [0, "00:01:00"];
            yield return [5, "00:00:00"];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidLoginPayloads))]
    public async Task Invalid_login_request_uses_global_validation(string payload, string field)
    {
        await using var fixture = await LoginFixture.CreateAsync();

        using var response = await fixture.Client.PostAsync("/api/auth/login", Json(payload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(responseText);
        Assert.Equal("validation_error", body.RootElement.GetProperty("error").GetString());
        Assert.Contains(field, body.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name),
            StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-password-marker", responseText, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidJwtConfigurations))]
    public async Task Invalid_jwt_configuration_prevents_api_startup(
        string issuer, string audience, string signingKey, string accessTokenMinutes)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => LoginFixture.CreateAsync(
            issuer: issuer,
            audience: audience,
            signingKey: signingKey,
            accessTokenMinutes: accessTokenMinutes));
    }

    [Theory]
    [MemberData(nameof(InvalidLoginRateLimitConfigurations))]
    public async Task Invalid_login_rate_limit_configuration_prevents_api_startup(int permitLimit, string window)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => LoginFixture.CreateAsync(
            loginPermitLimit: permitLimit,
            loginWindow: window));
    }

    [Fact]
    public async Task Valid_login_returns_public_user_fields_and_a_signed_token_for_the_existing_password_hash()
    {
        await using var fixture = await LoginFixture.CreateAsync();
        const string password = "correct horse battery staple";
        var userId = await RegisterAsync(fixture, "Ghaith", "Ghaith@Test.com", password);

        using var response = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new
        {
            email = "Ghaith@Test.com",
            password
        })));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(responseText);
        Assert.Equal("Bearer", body.RootElement.GetProperty("tokenType").GetString());
        Assert.Equal(userId, body.RootElement.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal("Ghaith", body.RootElement.GetProperty("user").GetProperty("displayName").GetString());
        Assert.Equal("Ghaith@Test.com", body.RootElement.GetProperty("user").GetProperty("email").GetString());
        Assert.DoesNotContain("password", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("version", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestSigningKey, responseText, StringComparison.Ordinal);

        var token = body.RootElement.GetProperty("accessToken").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(token));
        var claims = ReadAndVerifyToken(token);
        Assert.Equal(userId.ToString("D"), claims.GetProperty("sub").GetString());
        Assert.Equal("Ghaith", claims.GetProperty("name").GetString());
        Assert.Equal(TestIssuer, claims.GetProperty("iss").GetString());
        Assert.Equal(TestAudience, claims.GetProperty("aud").GetString());
        Assert.NotEqual(Guid.Empty, Guid.Parse(claims.GetProperty("jti").GetString()!));
        Assert.Equal(fixture.ExpectedUtcNow.ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
        Assert.Equal(fixture.ExpectedUtcNow.AddMinutes(60).ToUnixTimeSeconds(), claims.GetProperty("exp").GetInt64());
        Assert.Equal(fixture.ExpectedUtcNow.AddMinutes(60), body.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset());

        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.Equal(1, await context.Portfolios.CountAsync());
        var user = await context.Users.AsNoTracking().SingleAsync();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        Assert.Contains(hasher.VerifyHashedPassword(user, user.PasswordHash, password), new[]
        {
            PasswordVerificationResult.Success,
            PasswordVerificationResult.SuccessRehashNeeded
        });
    }

    [Fact]
    public async Task Login_email_matching_uses_registration_normalization()
    {
        await using var fixture = await LoginFixture.CreateAsync();
        var userId = await RegisterAsync(fixture, "Ghaith", "  Ghaith@Test.com  ", "correct horse battery staple");

        using var response = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new
        {
            email = "  ghaith@test.com  ",
            password = "correct horse battery staple"
        })));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(userId, body.RootElement.GetProperty("user").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_return_the_same_invalid_credentials_response()
    {
        await using var fixture = await LoginFixture.CreateAsync();
        await RegisterAsync(fixture, "Ghaith", "ghaith@example.com", "correct horse battery staple");

        using var wrongPassword = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new
        {
            email = "ghaith@example.com",
            password = "private-password-marker"
        })));
        using var unknownEmail = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new
        {
            email = "unknown@example.com",
            password = "private-password-marker"
        })));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        var wrongPasswordText = await wrongPassword.Content.ReadAsStringAsync();
        var unknownEmailText = await unknownEmail.Content.ReadAsStringAsync();
        Assert.Equal(wrongPasswordText, unknownEmailText);
        using var body = JsonDocument.Parse(wrongPasswordText);
        Assert.Equal("invalid_credentials", body.RootElement.GetProperty("error").GetString());
        Assert.Equal("The email or password is invalid.", body.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("private-password-marker", wrongPasswordText, StringComparison.Ordinal);
        Assert.DoesNotContain("ghaith@example.com", wrongPasswordText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", wrongPasswordText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repeated_login_attempts_from_the_same_ip_are_throttled_before_password_verification()
    {
        var hasher = new CountingPasswordHasher();
        await using var fixture = await LoginFixture.CreateAsync(passwordHasher: hasher, loginPermitLimit: 2);
        await RegisterAsync(fixture, "Ghaith", "ghaith@example.com", "correct-password");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new
            {
                email = "ghaith@example.com",
                password = "wrong-password"
            })));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = Json(JsonSerializer.Serialize(new
            {
                email = "GHAITH@EXAMPLE.COM",
                password = "wrong-password"
            }))
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.9");
        using var throttled = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        using var body = JsonDocument.Parse(await throttled.Content.ReadAsStringAsync());
        Assert.Equal("too_many_requests", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(2, hasher.VerifyCount);

        using var health = await fixture.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Login_rate_limit_separates_client_ips_forwarded_by_a_configured_proxy()
    {
        var hasher = new CountingPasswordHasher();
        await using var fixture = await LoginFixture.CreateAsync(
            passwordHasher: hasher,
            loginPermitLimit: 1,
            trustedProxyAddress: "127.0.0.1");
        await RegisterAsync(fixture, "Ghaith", "ghaith@example.com", "correct-password");

        using var firstClientAttempt = await SendLoginFromForwardedIpAsync(fixture, "203.0.113.10");
        using var repeatedClientAttempt = await SendLoginFromForwardedIpAsync(fixture, "203.0.113.10");
        using var secondClientAttempt = await SendLoginFromForwardedIpAsync(fixture, "203.0.113.11");

        Assert.Equal(HttpStatusCode.Unauthorized, firstClientAttempt.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, repeatedClientAttempt.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondClientAttempt.StatusCode);
        Assert.Equal(2, hasher.VerifyCount);
    }

    [Fact]
    public async Task Success_rehash_needed_replaces_the_existing_password_hash()
    {
        var hasher = new RehashingPasswordHasher();
        await using var fixture = await LoginFixture.CreateAsync(hasher);
        var userId = await RegisterAsync(fixture, "Ghaith", "ghaith@example.com", "old-password");

        using var response = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new
        {
            email = "ghaith@example.com",
            password = "old-password"
        })));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var user = await context.Users.AsNoTracking().SingleAsync(value => value.Id == userId);
        Assert.Equal("test-hash-2:old-password", user.PasswordHash);
    }

    [Fact]
    public async Task Concurrent_rehash_conflict_reloads_the_user_and_retries_the_hash_update()
    {
        var hasher = new RehashingPasswordHasher();
        var saveChangesInterceptor = new FailFirstRehashSaveInterceptor();
        await using var fixture = await LoginFixture.CreateAsync(hasher, saveChangesInterceptor);
        var userId = await RegisterAsync(fixture, "Ghaith", "ghaith@example.com", "old-password");

        using var response = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new
        {
            email = "ghaith@example.com",
            password = "old-password"
        })));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, saveChangesInterceptor.Conflicts);
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var user = await context.Users.AsNoTracking().SingleAsync(value => value.Id == userId);
        Assert.Equal("test-hash-3:old-password", user.PasswordHash);
    }

    [Fact]
    public async Task Authorize_accepts_a_valid_bearer_token_and_returns_a_safe_401_for_missing_tokens()
    {
        await using var fixture = await LoginFixture.CreateAsync();
        var userId = await RegisterAsync(fixture, "Ghaith", "ghaith@example.com", "correct horse battery staple");
        var accessToken = await LoginAndReadTokenAsync(fixture, "ghaith@example.com", "correct horse battery staple");

        using var missing = await fixture.Client.GetAsync("/tests/auth-protected");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(UnauthorizedJson, await missing.Content.ReadAsStringAsync());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tests/auth-protected");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var authorized = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        using var body = JsonDocument.Parse(await authorized.Content.ReadAsStringAsync());
        Assert.Equal(userId.ToString("D"), body.RootElement.GetProperty("sub").GetString());
    }

    [Fact]
    public async Task Malformed_expired_tampered_wrong_issuer_and_wrong_audience_tokens_return_safe_401()
    {
        await using var fixture = await LoginFixture.CreateAsync();
        var now = fixture.ExpectedUtcNow;
        var valid = CreateToken(TestIssuer, TestAudience, now.AddMinutes(20), now, TestSigningKey);
        var tampered = valid[..^1] + (valid[^1] == 'a' ? "b" : "a");
        var invalidTokens = new[]
        {
            "not-a-jwt",
            tampered,
            CreateToken(TestIssuer, TestAudience, now.AddMinutes(-1), now.AddHours(-1), TestSigningKey),
            CreateToken("Other.Issuer", TestAudience, now.AddMinutes(20), now, TestSigningKey),
            CreateToken(TestIssuer, "Other.Audience", now.AddMinutes(20), now, TestSigningKey)
        };

        foreach (var token in invalidTokens)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/tests/auth-protected");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(UnauthorizedJson, await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Market_routes_remain_public_after_bearer_authentication_is_enabled()
    {
        await using var fixture = await LoginFixture.CreateAsync();

        using var health = await fixture.Client.GetAsync("/health");
        using var quote = await fixture.Client.GetAsync("/api/stocks/AAPL/quote");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, quote.StatusCode);
    }

    [Fact]
    public async Task OpenApi_describes_login_contract_and_bearer_security_for_authorized_operations_only()
    {
        await using var fixture = await LoginFixture.CreateAsync();

        using var response = await fixture.Client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var login = root.GetProperty("paths").GetProperty("/api/auth/login").GetProperty("post");
        var responses = login.GetProperty("responses");
        foreach (var status in new[] { "200", "400", "401", "429", "500" })
            Assert.True(responses.TryGetProperty(status, out _), $"Missing OpenAPI response {status}.");

        var requestSchema = GetSchemaProperties(root, login.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"));
        Assert.Contains("email", requestSchema, StringComparer.Ordinal);
        Assert.Contains("password", requestSchema, StringComparer.Ordinal);
        var responseSchema = GetSchemaProperties(root, responses.GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"));
        Assert.Contains("accessToken", responseSchema, StringComparer.Ordinal);
        Assert.Contains("tokenType", responseSchema, StringComparer.Ordinal);
        Assert.Contains("expiresAtUtc", responseSchema, StringComparer.Ordinal);
        Assert.Contains("user", responseSchema, StringComparer.Ordinal);
        Assert.DoesNotContain("passwordHash", responseSchema, StringComparer.OrdinalIgnoreCase);

        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        var protectedOperation = root.GetProperty("paths").GetProperty("/tests/auth-protected").GetProperty("get");
        Assert.NotEmpty(protectedOperation.GetProperty("security").EnumerateArray());
        Assert.False(login.TryGetProperty("security", out _));
        Assert.False(root.GetProperty("paths").GetProperty("/api/stocks/{symbol}/quote").GetProperty("get")
            .TryGetProperty("security", out _));
    }

    private static string[] GetSchemaProperties(JsonElement root, JsonElement schema)
    {
        var schemaName = schema.GetProperty("$ref").GetString()!.Split('/').Last();
        return root.GetProperty("components").GetProperty("schemas").GetProperty(schemaName)
            .GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray();
    }

    private static async Task<Guid> RegisterAsync(LoginFixture fixture, string displayName, string email, string password)
    {
        using var response = await fixture.Client.PostAsync("/api/auth/register", Json(JsonSerializer.Serialize(new
        {
            displayName,
            email,
            password
        })));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> LoginAndReadTokenAsync(LoginFixture fixture, string email, string password)
    {
        using var response = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new { email, password })));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static Task<HttpResponseMessage> SendLoginFromForwardedIpAsync(LoginFixture fixture, string ipAddress)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = Json(JsonSerializer.Serialize(new
            {
                email = "ghaith@example.com",
                password = "wrong-password"
            }))
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", ipAddress);
        return fixture.Client.SendAsync(request);
    }

    private static JsonElement ReadAndVerifyToken(string token)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(DecodeBase64Url(parts[0]));
        Assert.Equal("HS256", header.RootElement.GetProperty("alg").GetString());
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSigningKey));
        var expectedSignature = hmac.ComputeHash(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
        Assert.True(CryptographicOperations.FixedTimeEquals(expectedSignature, DecodeBase64Url(parts[2])));
        using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
        return payload.RootElement.Clone();
    }

    private static string CreateToken(string issuer, string audience, DateTimeOffset expiresAtUtc,
        DateTimeOffset issuedAtUtc, string signingKey)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuer,
            aud = audience,
            sub = Guid.NewGuid().ToString("D"),
            jti = Guid.NewGuid().ToString("D"),
            name = "Token Test",
            iat = issuedAtUtc.ToUnixTimeSeconds(),
            exp = expiresAtUtc.ToUnixTimeSeconds()
        }));
        var signingInput = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        return $"{signingInput}.{Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput)))}";
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    private sealed class LoginFixture(SqliteConnection connection, WebApplicationFactory<Program> application,
        HttpClient client, DateTimeOffset expectedUtcNow) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public DateTimeOffset ExpectedUtcNow { get; } = expectedUtcNow;
        public IServiceScope CreateScope() => application.Services.CreateScope();

        public static async Task<LoginFixture> CreateAsync(
            IPasswordHasher<User>? passwordHasher = null,
            SaveChangesInterceptor? saveChangesInterceptor = null,
            string issuer = TestIssuer,
            string audience = TestAudience,
            string signingKey = TestSigningKey,
            string accessTokenMinutes = "60",
            int loginPermitLimit = 5,
            string loginWindow = "00:01:00",
            string? trustedProxyAddress = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var optionsBuilder = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection);
            if (saveChangesInterceptor is not null)
            {
                optionsBuilder.AddInterceptors(saveChangesInterceptor);
            }
            var options = optionsBuilder.Options;
            var now = DateTimeOffset.UtcNow;
            var expectedUtcNow = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute,
                now.Second, TimeSpan.Zero);

            var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StockLab"] = "Server=localhost;Database=LoginTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true",
                        ["Jwt:Issuer"] = issuer,
                        ["Jwt:Audience"] = audience,
                        ["Jwt:SigningKey"] = signingKey,
                        ["Jwt:AccessTokenMinutes"] = accessTokenMinutes,
                        ["LoginRateLimit:PermitLimit"] = loginPermitLimit.ToString(),
                        ["LoginRateLimit:Window"] = loginWindow,
                        ["ForwardedHeaders:KnownProxies:0"] = trustedProxyAddress
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<StockLabDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<StockLabDbContext>();
                    services.AddSingleton(options);
                    services.AddScoped<StockLabDbContext, SqliteLoginDbContext>();
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(expectedUtcNow));
                    services.AddControllers().AddApplicationPart(typeof(ProtectedApiProbeController).Assembly);
                    if (passwordHasher is not null)
                    {
                        services.RemoveAll<IPasswordHasher<User>>();
                        services.AddSingleton(passwordHasher);
                    }
                    if (trustedProxyAddress is not null)
                    {
                        services.AddSingleton<IStartupFilter>(
                            new FixedRemoteIpStartupFilter(IPAddress.Parse(trustedProxyAddress)));
                    }
                });
            });

            try
            {
                var client = application.CreateClient();
                using var scope = application.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Database.EnsureCreatedAsync();
                return new LoginFixture(connection, application, client, expectedUtcNow);
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

    private sealed class SqliteLoginDbContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().Property(user => user.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(portfolio => portfolio.Version).ValueGeneratedNever();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RehashingPasswordHasher : IPasswordHasher<User>
    {
        private int hashNumber;

        public string HashPassword(User user, string password) => $"test-hash-{Interlocked.Increment(ref hashNumber)}:{password}";

        public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword) =>
            providedPassword == "old-password"
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
    }

    private sealed class CountingPasswordHasher : IPasswordHasher<User>
    {
        private int verifyCount;

        public int VerifyCount => Volatile.Read(ref verifyCount);

        public string HashPassword(User user, string password) => $"test-hash:{password}";

        public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref verifyCount);
            return hashedPassword == $"test-hash:{providedPassword}"
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.Failed;
        }
    }

    private sealed class FixedRemoteIpStartupFilter(IPAddress remoteIpAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
        {
            application.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = remoteIpAddress;
                await nextMiddleware();
            });
            next(application);
        };
    }

    private sealed class FailFirstRehashSaveInterceptor : SaveChangesInterceptor
    {
        private int conflicts;

        public int Conflicts => Volatile.Read(ref conflicts);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var rehashIsPending = eventData.Context?.ChangeTracker.Entries<User>()
                .Any(entry => entry.State == EntityState.Modified && entry.Property(user => user.PasswordHash).IsModified) == true;
            if (rehashIsPending && Interlocked.CompareExchange(ref conflicts, 1, 0) == 0)
            {
                throw new DbUpdateConcurrencyException("Simulated concurrent password rehash.");
            }

            return ValueTask.FromResult(result);
        }
    }
}

[ApiController]
[Route("tests/auth-protected")]
[Authorize]
public sealed class ProtectedApiProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { sub = User.FindFirst("sub")?.Value });
}
