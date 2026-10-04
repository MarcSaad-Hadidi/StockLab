namespace StockLab.Application.Exceptions;

public sealed class PriceAlertAlreadyTriggeredException : Exception
{
    public PriceAlertAlreadyTriggeredException() : base("A triggered alert cannot be modified.") { }
}
