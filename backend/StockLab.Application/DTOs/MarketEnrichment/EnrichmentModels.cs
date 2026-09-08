namespace StockLab.Application.DTOs.MarketEnrichment;

public sealed record AnalystRatings(long? StrongBuy, long? Buy, long? Hold, long? Sell, long? StrongSell);
public sealed record StockFundamentals
{
    public required string Symbol { get; init; }
    public string? AssetType { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Exchange { get; init; }
    public string? Currency { get; init; }
    public string? Country { get; init; }
    public string? Sector { get; init; }
    public string? Industry { get; init; }
    public decimal? MarketCap { get; init; }
    public decimal? PeRatio { get; init; }
    public decimal? PegRatio { get; init; }
    public decimal? BookValue { get; init; }
    public decimal? DividendPerShare { get; init; }
    public decimal? DividendYield { get; init; }
    public decimal? EpsTtm { get; init; }
    public decimal? RevenuePerShareTtm { get; init; }
    public decimal? ProfitMargin { get; init; }
    public decimal? OperatingMarginTtm { get; init; }
    public decimal? ReturnOnAssetsTtm { get; init; }
    public decimal? ReturnOnEquityTtm { get; init; }
    public decimal? RevenueTtm { get; init; }
    public decimal? GrossProfitTtm { get; init; }
    public decimal? Ebitda { get; init; }
    public decimal? DilutedEpsTtm { get; init; }
    public decimal? QuarterlyEarningsGrowthYoy { get; init; }
    public decimal? QuarterlyRevenueGrowthYoy { get; init; }
    public decimal? Beta { get; init; }
    public decimal? FiftyTwoWeekHigh { get; init; }
    public decimal? FiftyTwoWeekLow { get; init; }
    public decimal? FiftyDayMovingAverage { get; init; }
    public decimal? TwoHundredDayMovingAverage { get; init; }
    public decimal? AnalystTargetPrice { get; init; }
    public long? SharesOutstanding { get; init; }
    public AnalystRatings? AnalystRatings { get; init; }
}
/// <summary>Credential-free public CDN URLs; missing artwork uses the caller's ticker fallback.</summary>
public sealed record CompanyLogo(string Symbol, string? PngUrl, string? SvgUrl);
public sealed record StockEarnings(string Symbol, DateOnly? NextEarningsDate, DateOnly? FiscalDateEnding, decimal? Estimate, string? Currency);
public sealed record MarketMover(string Symbol, decimal Price, decimal Change, decimal ChangePercent, long Volume);
/// <summary>Latest available EOD lists; never a live quote source.</summary>
public sealed record MarketMovers(string? LastUpdated, IReadOnlyList<MarketMover> Gainers, IReadOnlyList<MarketMover> Losers, IReadOnlyList<MarketMover> MostActive);
