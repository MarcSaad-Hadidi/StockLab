using StockLab.Application.DTOs.MarketEnrichment;

namespace StockLab.Api.DTOs;

/// <summary>The enrichment rankings use Alpha Vantage independently of the terminal quote provider.</summary>
public sealed record MarketMoversResponse(string? LastUpdated, IReadOnlyList<MarketMover> Gainers,
    IReadOnlyList<MarketMover> Losers, IReadOnlyList<MarketMover> MostActive)
{
    public string Source => "AlphaVantage";
}
