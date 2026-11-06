using Microsoft.EntityFrameworkCore;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Persistence;

public sealed class PersonalHistoryModelTests
{
    [Fact]
    public void Personal_observations_have_owned_unique_slots_and_twelve_decimal_amounts()
    {
        using var db = new StockLabDbContext(new DbContextOptionsBuilder<StockLabDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelOnlyNeverUsed;Integrated Security=true").Options);
        var model = db.Model.FindEntityType("StockLab.Domain.Entities.PortfolioValuationSnapshot");
        Assert.NotNull(model);
        Assert.Equal("PortfolioValuationSnapshots", model.GetTableName());
        foreach (var property in new[] { "CashBalance", "PositionsValue", "TotalValue" })
        {
            Assert.Equal("varchar(64)", model.FindProperty(property)!.GetColumnType());
            Assert.Equal(typeof(string), model.FindProperty(property)!.GetValueConverter()!.ProviderClrType);
        }
        Assert.Contains(model.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(new[] { "PortfolioId", "SlotUtc" }));
        Assert.Contains(model.GetForeignKeys(), key => key.Properties.Single().Name == "PortfolioId"
            && key.DeleteBehavior == DeleteBehavior.NoAction);
    }
}
