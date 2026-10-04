using System.Net;
using Microsoft.EntityFrameworkCore;
using static StockLab.UnitTests.Api.PriceAlertsApiTests;

namespace StockLab.UnitTests.Api;

/// <summary>Opt-in smoke against an explicitly supplied existing SQL Server/Azure SQL schema.</summary>
public sealed class PriceAlertsSqlServerApiTests
{
    private const string ConnectionVariable = "STOCKLAB_PRICE_ALERTS_SQL_CONNECTION";

    [SqlSmokeFact]
    public async Task Sql_login_CRUD_and_disabled_persistence_survive_restart()
    {
        await using var f = await PriceAlertsFixture.CreateAsync(sqlConnection: Environment.GetEnvironmentVariable(ConnectionVariable)!);
        await VerifyPersistentLifecycleAsync(f);
    }

    [SqlSmokeTheory]
    [InlineData("PUT", "")]
    [InlineData("POST", "/disable")]
    [InlineData("DELETE", "")]
    public async Task Sql_generated_rowversion_conflicts_return_409(string method, string suffix)
    {
        await using var f = await PriceAlertsFixture.CreateAsync(new ChangeAlertVersion(),
            sqlConnection: Environment.GetEnvironmentVariable(ConnectionVariable)!);
        var id = await CreateAlertAsync(f, f.TokenA);
        byte[] originalVersion;
        await using (var db = f.CreateDbContext())
            originalVersion = (await db.PriceAlerts.SingleAsync(a => a.Id == id)).Version;
        Assert.Equal(8, originalVersion.Length);
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        using var response = await SendAsync(f.Client, method, $"/api/alerts/{id}{suffix}", f.TokenA,
            """{"condition":"Below","targetPrice":180}""");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("alert_update_conflict", (await BodyAsync(response)).GetProperty("error").GetString());
        await using var verifier = f.CreateDbContext();
        var stored = await verifier.PriceAlerts.SingleAsync(a => a.Id == id);
        Assert.NotEqual(originalVersion, stored.Version);
        Assert.Equal("Active", stored.Status);
        Assert.Equal("Above", stored.Condition);
        Assert.Equal(250m, stored.TargetPrice);
        Assert.Equal(stored.CreatedAtUtc, stored.UpdatedAtUtc);
    }

    private sealed class SqlSmokeFactAttribute : FactAttribute
    {
        public SqlSmokeFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
                Skip = "Explicitly supply STOCKLAB_PRICE_ALERTS_SQL_CONNECTION to run the disposable SQL smoke.";
        }
    }

    private sealed class SqlSmokeTheoryAttribute : TheoryAttribute
    {
        public SqlSmokeTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
                Skip = "Explicitly supply STOCKLAB_PRICE_ALERTS_SQL_CONNECTION to test real SQL rowversion conflicts.";
        }
    }
}
