namespace StockLab.Application.Interfaces;

public interface IPasswordRecoveryRequestQueue
{
    Task<bool> TryEnqueueAsync(string email, string language, CancellationToken cancellationToken);
}
