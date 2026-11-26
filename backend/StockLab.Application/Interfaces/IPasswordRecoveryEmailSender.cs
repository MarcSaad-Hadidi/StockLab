namespace StockLab.Application.Interfaces;

public interface IPasswordRecoveryEmailSender
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
    Task SendAsync(string email, string language, Guid userId, string token, CancellationToken cancellationToken);
}
