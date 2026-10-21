using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiTraderPerformanceModelTests
{
    [Fact]
    public void Migration_only_adds_nullable_pnl_and_ai_snapshot_storage_without_model_drift()
    {
        using var db = new StockLabDbContext(new DbContextOptionsBuilder<StockLabDbContext>()
            .UseSqlServer("Server=localhost;Database=StockLabModelOnly;Integrated Security=true").Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var definition = Assert.Single(assembly.Migrations, m => m.Key.EndsWith("_AddAiTraderPerformanceMetrics"));
        var migration = assembly.CreateMigration(definition.Value, db.Database.ProviderName!);
        var column = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        Assert.Equal("AiTrades", column.Table);
        Assert.Equal("RealizedPnL", column.Name);
        Assert.True(column.IsNullable);
        Assert.Null(column.DefaultValue);
        Assert.Equal("decimal(19,4)", column.ColumnType);
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("AiPortfolioSnapshots", table.Name);
        var fk = Assert.Single(table.ForeignKeys);
        Assert.Equal("AiPortfolios", fk.PrincipalTable);
        Assert.Equal(ReferentialAction.NoAction, fk.OnDelete);
        Assert.All(migration.UpOperations, op => Assert.True(op is AddColumnOperation or CreateTableOperation or CreateIndexOperation));
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AiTraderValuationSnapshot))!;
        Assert.Equal("decimal(19,4)", entity.FindProperty("CashBalance")!.GetColumnType());
        Assert.Equal("decimal(19,4)", entity.FindProperty("PositionsValue")!.GetColumnType());
        Assert.Equal("datetime2(7)", entity.FindProperty("ValuedAtUtc")!.GetColumnType());
        Assert.Null(entity.FindProperty("TotalValue"));
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["CaptureId"]));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Real_api_composition_resolves_performance_services_with_scoped_lifetimes()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.ConfigureAppConfiguration((_, builder) =>
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StockLab"] = "Server=localhost;Database=StockLabCompositionOnly;Integrated Security=true;TrustServerCertificate=true",
                ["Jwt:SigningKey"] = "test-only-performance-key-at-least-32-bytes-long",
                ["MarketData:Provider"] = "Mock"
            })));
        using var first = app.Services.CreateScope();
        using var second = app.Services.CreateScope();
        var metrics = first.ServiceProvider.GetRequiredService<IAiTraderPerformanceService>();
        var history = first.ServiceProvider.GetRequiredService<IAiTraderValuationHistoryService>();
        Assert.IsType<AiTraderPerformanceService>(metrics);
        Assert.IsType<AiTraderValuationHistoryService>(history);
        Assert.Same(metrics, first.ServiceProvider.GetRequiredService<IAiTraderPerformanceService>());
        Assert.Same(history, first.ServiceProvider.GetRequiredService<IAiTraderValuationHistoryService>());
        Assert.NotSame(metrics, second.ServiceProvider.GetRequiredService<IAiTraderPerformanceService>());
        Assert.NotSame(history, second.ServiceProvider.GetRequiredService<IAiTraderValuationHistoryService>());
    }
}
