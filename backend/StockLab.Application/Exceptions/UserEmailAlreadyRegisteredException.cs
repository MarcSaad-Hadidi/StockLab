namespace StockLab.Application.Exceptions;

public sealed class UserEmailAlreadyRegisteredException : Exception
{
    public UserEmailAlreadyRegisteredException()
        : base("An account already exists for this email.")
    {
    }
}
