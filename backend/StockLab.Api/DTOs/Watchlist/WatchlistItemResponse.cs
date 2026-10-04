namespace StockLab.Api.DTOs.Watchlist;

public sealed record WatchlistItemResponse(string Symbol, DateTime CreatedAtUtc);
