namespace StockLab.Application.Interfaces;

public interface IUserPasswordRecoveryService
{
    Task<bool> ResetAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken);
}
