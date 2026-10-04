using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Trading;

public sealed class AiTradeExecutionModelTests
{
    [Fact]
    public void Trades_have_ai_only_ownership_unique_orders_and_safe_storage()
    {
        using var db = new StockLabDbContext(new DbContextOptionsBuilder<StockLabDbContext>()
            .UseSqlServer("Server=localhost;Database=StockLabModelOnly;Integrated Security=true").Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var trade = model.FindEntityType("StockLab.Domain.Entities.AiTrade");
        Assert.NotNull(trade);
        Assert.Equal("AiTrades", trade.GetTableName());
        Assert.Equal("Id", Assert.Single(trade.FindPrimaryKey()!.Properties).Name);
        Assert.True(trade.FindProperty("AiDecisionId")?.IsNullable);
        var decisionFk = Assert.Single(trade.GetForeignKeys(), f => f.PrincipalEntityType.GetTableName() == "AiDecisions");
        Assert.Equal(DeleteBehavior.NoAction, decisionFk.DeleteBehavior);
        var decisionIndex = Assert.Single(trade.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(["AiDecisionId"]));
        Assert.True(decisionIndex.IsUnique);
        Assert.Equal("[AiDecisionId] IS NOT NULL", decisionIndex.GetFilter());
        Assert.Single(trade.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(["ExecutedAtUtc", "Id"]));
        var fk = Assert.Single(trade.GetForeignKeys(), f => f.PrincipalEntityType.GetTableName() == "AiPortfolios");
        Assert.Equal("AiPortfolios", fk.PrincipalEntityType.GetTableName());
        Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
        Assert.True(trade.GetIndexes().Single(i => i.Properties.Select(p => p.Name)
            .SequenceEqual(["AiTraderPortfolioId", "OrderId"])).IsUnique);
        Assert.Equal("decimal(19,8)", trade.FindProperty("Quantity")!.GetColumnType());
        Assert.Equal("nvarchar(32)", trade.FindProperty("Symbol")!.GetColumnType());
        foreach (var name in new[] { "ExecutionPrice", "TotalAmount" })
            Assert.Equal("decimal(19,4)", trade.FindProperty(name)!.GetColumnType());
        Assert.Equal("datetime2(7)", trade.FindProperty("ExecutedAtUtc")!.GetColumnType());
        foreach (var sql in new[] { "[Quantity] > 0", "[ExecutionPrice] > 0", "[TotalAmount] > 0", "[Side] IN ('BUY', 'SELL')" })
            Assert.Contains(trade.GetCheckConstraints(), c => c.Sql == sql);
        var assembly = db.GetService<IMigrationsAssembly>();
        var definition = Assert.Single(assembly.Migrations, m => m.Key.EndsWith("_AddAiTrades"));
        var migration = assembly.CreateMigration(definition.Value, db.Database.ProviderName!);
        Assert.Equal("AiTrades", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation));
        Assert.Equal("AiTrades", Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
        var historyDefinition = Assert.Single(assembly.Migrations, m => m.Key.EndsWith("_IndexAiTradeHistory"));
        var historyMigration = assembly.CreateMigration(historyDefinition.Value, db.Database.ProviderName!);
        var historyIndex = Assert.IsType<CreateIndexOperation>(Assert.Single(historyMigration.UpOperations));
        Assert.Equal("AiTrades", historyIndex.Table);
        Assert.Equal(new[] { "ExecutedAtUtc", "Id" }, historyIndex.Columns);
        Assert.False(historyIndex.IsUnique);
        var removeHistoryIndex = Assert.IsType<DropIndexOperation>(Assert.Single(historyMigration.DownOperations));
        Assert.Equal(historyIndex.Name, removeHistoryIndex.Name);
        Assert.Equal("AiTrades", removeHistoryIndex.Table);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
