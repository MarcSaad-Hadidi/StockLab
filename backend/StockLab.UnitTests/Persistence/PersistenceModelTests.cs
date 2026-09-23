using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Persistence;

public sealed class PersistenceModelTests
{
    private static StockLabDbContext CreateContext() => new(
        new DbContextOptionsBuilder<StockLabDbContext>()
            .UseSqlServer("Server=localhost;Database=StockLabModelOnly;Integrated Security=true;TrustServerCertificate=true")
            .Options);

    [Theory]
    [InlineData(typeof(User), "Users")]
    [InlineData(typeof(Portfolio), "Portfolios")]
    [InlineData(typeof(Holding), "Holdings")]
    [InlineData(typeof(Transaction), "Transactions")]
    [InlineData(typeof(Watchlist), "Watchlists")]
    [InlineData(typeof(PriceAlert), "PriceAlerts")]
    public void Supported_entities_have_guid_primary_keys_and_expected_tables(Type entityType, string table)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(entityType)!;

        Assert.NotNull(entity);
        Assert.Equal(table, entity.GetTableName());
        Assert.Equal([nameof(User.Id)], entity.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        Assert.Equal(typeof(Guid), entity.FindProperty(nameof(User.Id))!.ClrType);
    }

    [Fact]
    public void Financial_columns_have_exact_sql_server_types()
    {
        using var context = CreateContext();

        Assert.Equal("char(3)", Property<Portfolio>(context, nameof(Portfolio.Currency)).GetColumnType());
        Assert.Equal("decimal(19,4)", Property<Portfolio>(context, nameof(Portfolio.CashBalance)).GetColumnType());
        Assert.Equal("decimal(19,4)", Property<Portfolio>(context, nameof(Portfolio.InitialCapital)).GetColumnType());
        Assert.Equal("decimal(19,8)", Property<Holding>(context, nameof(Holding.Quantity)).GetColumnType());
        Assert.Equal("decimal(19,4)", Property<Holding>(context, nameof(Holding.AverageCost)).GetColumnType());
        Assert.Equal("decimal(19,8)", Property<Transaction>(context, nameof(Transaction.Quantity)).GetColumnType());
        Assert.Equal("decimal(19,4)", Property<Transaction>(context, nameof(Transaction.ExecutionPrice)).GetColumnType());
        Assert.Equal("decimal(19,4)", Property<Transaction>(context, nameof(Transaction.TotalAmount)).GetColumnType());
        Assert.Equal("decimal(19,4)", Property<PriceAlert>(context, nameof(PriceAlert.TargetPrice)).GetColumnType());
        Assert.Equal("decimal(19,4)", Property<PriceAlert>(context, nameof(PriceAlert.TriggeredPrice)).GetColumnType());
    }

    [Fact]
    public void Strings_and_dates_follow_schema()
    {
        using var context = CreateContext();

        Assert.Equal(100, Property<User>(context, nameof(User.DisplayName)).GetMaxLength());
        Assert.Equal(254, Property<User>(context, nameof(User.Email)).GetMaxLength());
        Assert.Equal(254, Property<User>(context, nameof(User.NormalizedEmail)).GetMaxLength());
        Assert.Equal(1024, Property<User>(context, nameof(User.PasswordHash)).GetMaxLength());
        Assert.Equal("nvarchar(32)", Property<Holding>(context, nameof(Holding.Symbol)).GetColumnType());
        Assert.Equal("nvarchar(32)", Property<Transaction>(context, nameof(Transaction.Symbol)).GetColumnType());
        Assert.Equal("nvarchar(32)", Property<Watchlist>(context, nameof(Watchlist.Symbol)).GetColumnType());
        Assert.Equal("nvarchar(32)", Property<PriceAlert>(context, nameof(PriceAlert.Symbol)).GetColumnType());
        Assert.Equal("datetime2(7)", Property<User>(context, nameof(User.CreatedAtUtc)).GetColumnType());
        Assert.Equal("datetime2(7)", Property<Portfolio>(context, nameof(Portfolio.CreatedAtUtc)).GetColumnType());
        Assert.Equal("datetime2(7)", Property<Transaction>(context, nameof(Transaction.ExecutedAtUtc)).GetColumnType());
        Assert.Equal("datetime2(7)", Property<PriceAlert>(context, nameof(PriceAlert.TriggeredAtUtc)).GetColumnType());
    }

    [Fact]
    public void Unique_indexes_protect_registration_portfolios_orders_and_symbols()
    {
        using var context = CreateContext();

        AssertUniqueIndex<User>(context, nameof(User.NormalizedEmail));
        AssertUniqueIndex<Portfolio>(context, nameof(Portfolio.UserId));
        AssertUniqueIndex<Holding>(context, nameof(Holding.PortfolioId), nameof(Holding.Symbol));
        AssertUniqueIndex<Transaction>(context, nameof(Transaction.PortfolioId), nameof(Transaction.OrderId));
        AssertUniqueIndex<Watchlist>(context, nameof(Watchlist.UserId), nameof(Watchlist.Symbol));
    }

    [Fact]
    public void All_foreign_keys_prevent_database_cascades()
    {
        using var context = CreateContext();

        foreach (var entity in context.Model.GetEntityTypes())
        {
            foreach (var key in entity.GetForeignKeys())
            {
                Assert.Equal(DeleteBehavior.NoAction, key.DeleteBehavior);
            }
        }

        AssertForeignKey<Portfolio>(context, nameof(Portfolio.UserId), typeof(User));
        AssertForeignKey<Holding>(context, nameof(Holding.PortfolioId), typeof(Portfolio));
        AssertForeignKey<Transaction>(context, nameof(Transaction.PortfolioId), typeof(Portfolio));
        AssertForeignKey<Watchlist>(context, nameof(Watchlist.UserId), typeof(User));
        AssertForeignKey<PriceAlert>(context, nameof(PriceAlert.UserId), typeof(User));
    }

    [Fact]
    public void Mutable_aggregates_use_sql_server_rowversion()
    {
        using var context = CreateContext();

        foreach (var property in new[]
        {
            Property<User>(context, nameof(User.Version)),
            Property<Portfolio>(context, nameof(Portfolio.Version)),
            Property<PriceAlert>(context, nameof(PriceAlert.Version))
        })
        {
            Assert.Equal("rowversion", property.GetColumnType());
            Assert.True(property.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        }
    }

    [Fact]
    public void Query_indexes_and_alert_filter_are_present()
    {
        using var context = CreateContext();

        AssertIndex<Transaction>(context, nameof(Transaction.PortfolioId), nameof(Transaction.ExecutedAtUtc), nameof(Transaction.Id));
        AssertIndex<Transaction>(context, nameof(Transaction.PortfolioId), nameof(Transaction.Symbol), nameof(Transaction.ExecutedAtUtc));
        AssertIndex<Transaction>(context, nameof(Transaction.PortfolioId), nameof(Transaction.Side), nameof(Transaction.ExecutedAtUtc));
        AssertIndex<PriceAlert>(context, nameof(PriceAlert.UserId), nameof(PriceAlert.Status));
        var monitorIndex = FindIndex<PriceAlert>(context, nameof(PriceAlert.Symbol), nameof(PriceAlert.Currency));
        Assert.Contains("Active", monitorIndex.GetFilter());
    }

    [Fact]
    public void Sql_server_ddl_contains_business_checks_without_cascading_deletes()
    {
        using var context = CreateContext();
        var script = context.Database.GenerateCreateScript();

        Assert.Contains("CK_Holdings_Quantity", script);
        Assert.Contains("CK_Transactions_Side", script);
        Assert.Contains("CK_PriceAlerts_TriggeredFields", script);
        Assert.DoesNotContain("ON DELETE CASCADE", script, StringComparison.OrdinalIgnoreCase);
    }

    private static IProperty Property<TEntity>(StockLabDbContext context, string name) =>
        context.Model.FindEntityType(typeof(TEntity))!.FindProperty(name)!;

    private static void AssertUniqueIndex<TEntity>(StockLabDbContext context, params string[] properties) =>
        Assert.True(FindIndex<TEntity>(context, properties).IsUnique);

    private static void AssertIndex<TEntity>(StockLabDbContext context, params string[] properties) =>
        Assert.NotNull(FindIndex<TEntity>(context, properties));

    private static IIndex FindIndex<TEntity>(StockLabDbContext context, params string[] properties) =>
        context.Model.FindEntityType(typeof(TEntity))!.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(properties));

    private static void AssertForeignKey<TEntity>(StockLabDbContext context, string property, Type principal) =>
        Assert.Contains(context.Model.FindEntityType(typeof(TEntity))!.GetForeignKeys(),
            key => key.Properties.Single().Name == property && key.PrincipalEntityType.ClrType == principal);
}
