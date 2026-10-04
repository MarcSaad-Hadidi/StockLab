namespace StockLab.Application.Exceptions;

public sealed class PriceAlertUpdateConflictException : Exception
{
    public PriceAlertUpdateConflictException() : base("The alert changed while the request was being processed.") { }
}
