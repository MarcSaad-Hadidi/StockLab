namespace StockLab.Application.Exceptions;

public sealed class WatchlistItemAlreadyExistsException : Exception
{
    public WatchlistItemAlreadyExistsException()
        : base("This symbol is already in your watchlist.")
    {
    }
}
