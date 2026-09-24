namespace StockLab.Application.Exceptions;

public sealed class UserProfileUpdateConflictException : Exception
{
    public UserProfileUpdateConflictException()
        : base("The profile was modified by another request.")
    {
    }
}
