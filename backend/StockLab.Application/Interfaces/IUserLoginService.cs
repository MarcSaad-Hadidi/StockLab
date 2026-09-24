using StockLab.Domain.Entities;

namespace StockLab.Application.Interfaces;

public interface IUserLoginService
{
    Task<User?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken);
}
