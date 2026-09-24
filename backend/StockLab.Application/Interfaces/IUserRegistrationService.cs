using StockLab.Domain.Entities;

namespace StockLab.Application.Interfaces;

public interface IUserRegistrationService
{
    Task<User> RegisterAsync(string displayName, string email, string password, CancellationToken cancellationToken);
}
