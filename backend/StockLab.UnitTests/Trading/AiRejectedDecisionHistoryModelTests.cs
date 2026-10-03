using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Trading;

public sealed class AiRejectedDecisionHistoryModelTests
{
    [Fact]
    public void Storage_is_normalized_required_one_to_one_and_does_not_cascade()
    {
        using var db = Context();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AiRejectedDecision))!;
        Assert.Equal("AiRejectedDecisions", entity.GetTableName());
        Assert.Equal(["AiDecisionId", "RejectedAtUtc", "RejectionReason"], entity.GetProperties().Select(p => p.Name).Order());
        Assert.Equal(["AiDecisionId"], entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(ValueGenerated.Never, entity.FindProperty("AiDecisionId")!.ValueGenerated);
        Assert.Equal("varchar(64)", entity.FindProperty("RejectionReason")!.GetColumnType());
        Assert.Equal("datetime2(7)", entity.FindProperty("RejectedAtUtc")!.GetColumnType());
        Assert.All(entity.GetProperties(), p => Assert.False(p.IsNullable));
        var fk = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(AiDecision), fk.PrincipalEntityType.ClrType);
        Assert.Equal(["Id"], fk.PrincipalKey.Properties.Select(p => p.Name));
        Assert.True(fk.IsUnique);
        Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
        Assert.Empty(entity.GetNavigations());
        var sql = Assert.Single(entity.GetCheckConstraints()).Sql;
        foreach (var reason in Enum.GetNames<AiRiskRejectionReason>()) Assert.Contains($"'{reason}'", sql);
        Assert.Contains("COLLATE Latin1_General_100_BIN2", sql);
        Assert.Contains("DATALENGTH([RejectionReason]) = LEN([RejectionReason])", sql);
        Assert.Equal(["RejectedAtUtc", "AiDecisionId"], Assert.Single(entity.GetIndexes()).Properties.Select(p => p.Name));
    }

    [Fact]
    public void Migration_creates_only_rejection_table_and_index_and_snapshot_matches()
    {
        using var db = Context();
        var assembly = db.GetService<IMigrationsAssembly>();
        var definition = Assert.Single(assembly.Migrations, m => m.Key.EndsWith("_AddAiRejectedDecisions"));
        var migration = assembly.CreateMigration(definition.Value, db.Database.ProviderName!);
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("AiRejectedDecisions", table.Name);
        Assert.Equal(3, table.Columns.Count);
        var fk = Assert.Single(table.ForeignKeys);
        Assert.Equal("AiDecisions", fk.PrincipalTable);
        Assert.Equal(ReferentialAction.NoAction, fk.OnDelete);
        Assert.Single(table.CheckConstraints);
        Assert.Equal(2, migration.UpOperations.Count);
        Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation));
        Assert.Equal("AiRejectedDecisions", Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static StockLabDbContext Context() => new(new DbContextOptionsBuilder<StockLabDbContext>()
        .UseSqlServer("Server=localhost;Database=StockLabModelOnly;Integrated Security=true").Options);
}
