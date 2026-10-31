namespace StockLab.Application.Interfaces;

public interface IUserSessionValidator
{
    Task<bool> IsCurrentAsync(Guid userId, long sessionVersion, CancellationToken cancellationToken);
}
