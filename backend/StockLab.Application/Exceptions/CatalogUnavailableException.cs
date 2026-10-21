namespace StockLab.Application.Exceptions;

public sealed class CatalogUnavailableException(string catalogName, bool isInvalid = false)
    : Exception(isInvalid ? "The configured metadata source is invalid." : "The metadata source is unavailable.")
{
    public string CatalogName { get; } = catalogName;
    public bool IsInvalid { get; } = isInvalid;
}
