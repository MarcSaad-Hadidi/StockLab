using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Persistence;

public sealed class AiTraderPortfolioModelTests
{
    [Fact]
    public void Migration_only_adds_ai_tables_and_snapshot_matches_the_model()
    {
        using var context = new StockLabDbContext(new DbContextOptionsBuilder<StockLabDbContext>()
            .UseSqlServer("Server=localhost;Database=StockLabModelOnly;Integrated Security=true").Options);
        var assembly = context.GetService<IMigrationsAssembly>();
        var definition = Assert.Single(assembly.Migrations, m => m.Key.EndsWith("_AddAiTraderPortfolio"));
        var migration = assembly.CreateMigration(definition.Value, context.Database.ProviderName!);
        Assert.Equal(["AiTraderPortfolios", "AiTraderPositions"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(o => o.Name));
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
        Assert.Equal(["AiTraderPositions", "AiTraderPortfolios"],
            migration.DownOperations.Select(o => Assert.IsType<DropTableOperation>(o).Name));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Ai_schema_is_independent_and_protects_capital_positions_and_concurrency()
    {
        using var context = new StockLabDbContext(new DbContextOptionsBuilder<StockLabDbContext>()
            .UseSqlServer("Server=localhost;Database=StockLabModelOnly;Integrated Security=true").Options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var portfolio = model.FindEntityType("StockLab.Domain.Entities.AiTraderPortfolio");
        var position = model.FindEntityType("StockLab.Domain.Entities.AiTraderPosition");
        Assert.NotNull(portfolio);
        Assert.NotNull(position);
        Assert.Equal("AiTraderPortfolios", portfolio.GetTableName());
        Assert.Equal("AiTraderPositions", position.GetTableName());
        Assert.Null(portfolio.FindProperty("UserId"));
        Assert.Empty(portfolio.GetForeignKeys());
        Assert.Equal(["Positions"], portfolio.GetNavigations().Select(n => n.Name));
        Assert.Equal(portfolio, Assert.Single(position.GetForeignKeys()).PrincipalEntityType);
        Assert.True(portfolio.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(["PortfolioKey"])).IsUnique);
        Assert.True(position.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(["AiTraderPortfolioId", "Symbol"])).IsUnique);
        Assert.Equal("decimal(19,4)", portfolio.FindProperty("InitialCapital")!.GetColumnType());
        Assert.Equal("decimal(19,4)", portfolio.FindProperty("CashBalance")!.GetColumnType());
        Assert.Equal("decimal(19,8)", position.FindProperty("Quantity")!.GetColumnType());
        Assert.Equal("decimal(19,4)", position.FindProperty("AverageCost")!.GetColumnType());
        Assert.Equal("nvarchar(32)", position.FindProperty("Symbol")!.GetColumnType());
        var version = portfolio.FindProperty("Version")!;
        Assert.Equal("rowversion", version.GetColumnType());
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        Assert.Contains(portfolio.GetCheckConstraints(), c => c.Sql == "[CashBalance] >= 0");
        Assert.Contains(portfolio.GetCheckConstraints(), c => c.Sql == "[InitialCapital] = 100000");
        Assert.Contains(position.GetCheckConstraints(), c => c.Sql == "[Quantity] > 0");
        Assert.Contains(position.GetCheckConstraints(), c => c.Sql == "[AverageCost] > 0");
        foreach (var name in new[] { "CurrentPrice", "MarketValue", "UnrealizedPnL", "TotalValue", "PnL" })
        {
            Assert.Null(portfolio.FindProperty(name));
            Assert.Null(position.FindProperty(name));
        }
    }
}
