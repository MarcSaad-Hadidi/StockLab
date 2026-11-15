namespace StockLab.Application.Interfaces;

public interface IPasswordRecoveryEmailSender
{
    Task SendAsync(string email, string language, Guid userId, string token, CancellationToken cancellationToken);
}
