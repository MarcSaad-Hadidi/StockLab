namespace StockLab.Application.Exceptions;

public sealed class GoogleAuthenticationException(string error) : Exception("Google authentication could not be completed.")
{
    public string Error { get; } = error;
}
