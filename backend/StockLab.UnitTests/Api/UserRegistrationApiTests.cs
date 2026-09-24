using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Api;

public sealed class UserRegistrationApiTests
{
    public static IEnumerable<object[]> InvalidRegistrationPayloads
    {
        get
        {
            const string password = "private-password-marker";
            yield return ["{}", "displayName"];
            yield return [JsonSerializer.Serialize(new { displayName = "   ", email = "person@example.com", password }), "displayName"];
            yield return [JsonSerializer.Serialize(new { displayName = new string('x', 101), email = "person@example.com", password }), "displayName"];
            yield return [JsonSerializer.Serialize(new { displayName = "Person", password }), "email"];
            yield return [JsonSerializer.Serialize(new { displayName = "Person", email = "not-an-email", password }), "email"];
            yield return [JsonSerializer.Serialize(new { displayName = "Person", email = new string('x', 255), password }), "email"];
            yield return [JsonSerializer.Serialize(new { displayName = "Person", email = "person@example.com" }), "password"];
            yield return [JsonSerializer.Serialize(new { displayName = "Person", email = "person@example.com", password = "" }), "password"];
            yield return [JsonSerializer.Serialize(new { displayName = "Person", email = "person@example.com", password = "   " }), "password"];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidRegistrationPayloads))]
    public async Task Invalid_registration_uses_global_validation_response(string payload, string field)
    {
        await using var fixture = await RegistrationFixture.CreateAsync();

        using var response = await fixture.Client.PostAsync("/api/auth/register", Json(payload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(responseText);
        Assert.Equal("validation_error", body.RootElement.GetProperty("error").GetString());
        Assert.Equal("The request contains invalid data.", body.RootElement.GetProperty("message").GetString());
        Assert.Contains(field, body.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name),
            StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-password-marker", responseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Valid_registration_persists_a_hashed_user_and_returns_only_public_fields()
    {
        await using var fixture = await RegistrationFixture.CreateAsync();
        const string password = "correct horse battery staple";
        var payload = JsonSerializer.Serialize(new
        {
            displayName = "  Ghaith  ",
            email = "  Ghaith@Test.com  ",
            password
        });

        using var response = await fixture.Client.PostAsync("/api/auth/register", Json(payload));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(responseText);
        var responseFields = body.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(4, responseFields.Length);
        Assert.Contains("id", responseFields, StringComparer.Ordinal);
        Assert.Contains("displayName", responseFields, StringComparer.Ordinal);
        Assert.Contains("email", responseFields, StringComparer.Ordinal);
        Assert.Contains("createdAtUtc", responseFields, StringComparer.Ordinal);
        Assert.DoesNotContain("password", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("version", responseText, StringComparison.OrdinalIgnoreCase);

        var id = Guid.Parse(body.RootElement.GetProperty("id").GetString()!);
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("Ghaith", body.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("Ghaith@Test.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal(RegistrationFixture.ExpectedUtcNow.UtcDateTime,
            body.RootElement.GetProperty("createdAtUtc").GetDateTime());

        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var user = await context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(id, user.Id);
        Assert.Equal("Ghaith", user.DisplayName);
        Assert.Equal("Ghaith@Test.com", user.Email);
        Assert.Equal("GHAITH@TEST.COM", user.NormalizedEmail);
        Assert.Equal(RegistrationFixture.ExpectedUtcNow.UtcDateTime, user.CreatedAtUtc);
        Assert.Equal(user.CreatedAtUtc, user.UpdatedAtUtc);
        Assert.NotEqual(password, user.PasswordHash);

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        Assert.Contains(verification, new[]
        {
            PasswordVerificationResult.Success,
            PasswordVerificationResult.SuccessRehashNeeded
        });
    }

    [Fact]
    public async Task Email_duplicates_ignore_case_and_surrounding_whitespace()
    {
        await using var fixture = await RegistrationFixture.CreateAsync();
        using var first = await fixture.Client.PostAsync("/api/auth/register", Json(JsonSerializer.Serialize(new
        {
            displayName = "First User",
            email = "Ghaith@Test.com",
            password = "first secret"
        })));
        using var duplicate = await fixture.Client.PostAsync("/api/auth/register", Json(JsonSerializer.Serialize(new
        {
            displayName = "Second User",
            email = "  ghaith@test.com  ",
            password = "second secret"
        })));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var responseText = await duplicate.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(responseText);
        Assert.Equal("email_already_registered", body.RootElement.GetProperty("error").GetString());
        Assert.Equal("An account already exists for this email.", body.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("ghaith", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", responseText, StringComparison.OrdinalIgnoreCase);

        using var scope = fixture.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task Unique_constraint_race_returns_controlled_conflict()
    {
        await using var fixture = await RegistrationFixture.CreateAsync(new InsertDuplicateEmailBeforeSave());
        using var response = await fixture.Client.PostAsync("/api/auth/register", Json(JsonSerializer.Serialize(new
        {
            displayName = "Race User",
            email = "race@example.com",
            password = "race secret"
        })));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(responseText);
        Assert.Equal("email_already_registered", body.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("Sqlite", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("race@example.com", responseText, StringComparison.OrdinalIgnoreCase);
        using var scope = fixture.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task OpenApi_describes_registration_contract_and_controlled_status_codes()
    {
        await using var fixture = await RegistrationFixture.CreateAsync();
        using var response = await fixture.Client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths").GetProperty("/api/auth/register").GetProperty("post");
        var responses = operation.GetProperty("responses");
        foreach (var status in new[] { "201", "400", "409", "500" })
            Assert.True(responses.TryGetProperty(status, out _), $"Missing OpenAPI response {status}.");

        var successSchemaReference = responses.GetProperty("201").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var successSchemaName = successSchemaReference.Split('/').Last();
        var successProperties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(successSchemaName).GetProperty("properties");
        var successPropertyNames = successProperties.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(4, successPropertyNames.Length);
        Assert.DoesNotContain("password", successPropertyNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", successPropertyNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("version", successPropertyNames, StringComparer.OrdinalIgnoreCase);

        var requestSchemaReference = operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var requestSchemaName = requestSchemaReference.Split('/').Last();
        var requestProperties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(requestSchemaName).GetProperty("properties");
        var requestPropertyNames = requestProperties.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Contains("displayName", requestPropertyNames, StringComparer.Ordinal);
        Assert.Contains("email", requestPropertyNames, StringComparer.Ordinal);
        Assert.Contains("password", requestPropertyNames, StringComparer.Ordinal);
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    private sealed class RegistrationFixture(SqliteConnection connection, WebApplicationFactory<Program> application,
        HttpClient client) : IAsyncDisposable
    {
        public static DateTimeOffset ExpectedUtcNow { get; } = new(2026, 9, 23, 15, 30, 0, TimeSpan.Zero);
        public HttpClient Client { get; } = client;
        public IServiceScope CreateScope() => application.Services.CreateScope();

        public static async Task<RegistrationFixture> CreateAsync(SaveChangesInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var optionsBuilder = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection);
            if (interceptor is not null)
                optionsBuilder.AddInterceptors(interceptor);
            var options = optionsBuilder.Options;

            var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StockLab"] = "Server=localhost;Database=RegistrationTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true"
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<StockLabDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<StockLabDbContext>();
                    services.AddSingleton(options);
                    services.AddScoped<StockLabDbContext, SqliteRegistrationDbContext>();
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(ExpectedUtcNow));
                });
            });

            try
            {
                var client = application.CreateClient();
                using var scope = application.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Database.EnsureCreatedAsync();
                return new RegistrationFixture(connection, application, client);
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

    private sealed class SqliteRegistrationDbContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().Property(user => user.Version).ValueGeneratedNever();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class InsertDuplicateEmailBeforeSave : SaveChangesInterceptor
    {
        private int inserted;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null && Interlocked.Exchange(ref inserted, 1) == 0)
            {
                var connection = (SqliteConnection)eventData.Context.Database.GetDbConnection();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO "Users" ("Id", "DisplayName", "Email", "NormalizedEmail", "PasswordHash", "CreatedAtUtc", "UpdatedAtUtc", "Version")
                    VALUES ($id, $displayName, $email, $normalizedEmail, $passwordHash, $createdAtUtc, $updatedAtUtc, $version)
                    """;
                var now = RegistrationFixture.ExpectedUtcNow.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
                command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                command.Parameters.AddWithValue("$displayName", "Concurrent User");
                command.Parameters.AddWithValue("$email", "race@example.com");
                command.Parameters.AddWithValue("$normalizedEmail", "RACE@EXAMPLE.COM");
                command.Parameters.AddWithValue("$passwordHash", "test-only-hash");
                command.Parameters.AddWithValue("$createdAtUtc", now);
                command.Parameters.AddWithValue("$updatedAtUtc", now);
                command.Parameters.AddWithValue("$version", new byte[8]);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
