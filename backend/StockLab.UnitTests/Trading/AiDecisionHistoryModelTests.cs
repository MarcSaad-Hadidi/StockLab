using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Trading;

public sealed class AiDecisionHistoryModelTests
{
    [Fact]
    public void Decision_storage_has_exact_types_required_identity_checks_and_no_foreign_keys()
    {
        using var db = Context();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AiDecision))!;
        Assert.Equal("AiDecisions", entity.GetTableName());
        var id = Assert.Single(entity.FindPrimaryKey()!.Properties);
        Assert.Equal("Id", id.Name);
        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
        Assert.Empty(entity.GetForeignKeys());
        foreach (var (name, type) in new[] { ("Symbol", "nvarchar(32)"), ("Signal", "varchar(4)"),
            ("Confidence", "decimal(29,28)"), ("DecisionDate", "date"), ("ModelName", "nvarchar(128)"),
            ("ModelVersion", "nvarchar(128)"), ("RecordedAtUtc", "datetime2(7)") })
        {
            var property = entity.FindProperty(name)!;
            Assert.Equal(type, property.GetColumnType());
            Assert.False(property.IsNullable);
        }
        Assert.Equal(29, entity.FindProperty("Confidence")!.GetPrecision());
        Assert.Equal(28, entity.FindProperty("Confidence")!.GetScale());
        foreach (var sql in new[] { "[Confidence] >= 0 AND [Confidence] <= 1",
            "[Signal] COLLATE Latin1_General_100_BIN2 IN ('BUY', 'SELL', 'HOLD') AND DATALENGTH([Signal]) = CASE WHEN [Signal] = 'BUY' THEN 3 ELSE 4 END",
            "LTRIM(RTRIM([ModelName])) <> ''", "LTRIM(RTRIM([ModelVersion])) <> ''", "LTRIM(RTRIM([Symbol])) <> ''" })
            Assert.Contains(entity.GetCheckConstraints(), c => c.Sql == sql);
        var index = Assert.Single(entity.GetIndexes());
        Assert.Equal(["DecisionDate", "RecordedAtUtc", "Id"], index.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Migration_adds_only_decisions_and_down_drops_only_decisions_without_model_drift()
    {
        using var db = Context();
        var assembly = db.GetService<IMigrationsAssembly>();
        var definition = Assert.Single(assembly.Migrations, m => m.Key.EndsWith("_AddAiDecisions"));
        var migration = assembly.CreateMigration(definition.Value, db.Database.ProviderName!);
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("AiDecisions", table.Name);
        Assert.Empty(table.ForeignKeys);
        Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation));
        Assert.Equal("AiDecisions", Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static StockLabDbContext Context() => new(new DbContextOptionsBuilder<StockLabDbContext>()
        .UseSqlServer("Server=localhost;Database=StockLabModelOnly;Integrated Security=true").Options);
}
