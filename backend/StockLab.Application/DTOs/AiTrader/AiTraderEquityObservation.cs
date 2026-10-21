namespace StockLab.Application.DTOs.AiTrader;

public sealed record AiTraderEquityObservation(Guid Id, Guid PortfolioId, Guid CaptureId,
    DateTime ValuedAtUtc, string Currency, decimal CashBalance, decimal PositionsValue,
    decimal TotalValue, decimal PnL, decimal Return, bool IsInitialBaseline,
    IReadOnlyList<AiTraderQuoteObservation> QuoteObservations);

public sealed record AiTraderQuoteObservation(string Symbol, decimal Quantity, decimal AverageCost,
    decimal Price, DateTimeOffset AsOfUtc);
