using StockLab.Application.DTOs.MarketEnrichment;
namespace StockLab.Application.Interfaces;

/// <summary>Slow-changing enrichment, independent of the quote/search/history provider.</summary>
public interface IMarketEnrichmentProvider
{
    Task<StockFundamentals?> GetFundamentalsAsync(string symbol, CancellationToken cancellationToken = default);
    Task<CompanyLogo> GetLogoAsync(string symbol, CancellationToken cancellationToken = default);
    Task<StockEarnings> GetEarningsAsync(string symbol, CancellationToken cancellationToken = default);
    Task<MarketMovers> GetMoversAsync(CancellationToken cancellationToken = default);
}
