namespace StockLab.Application.Interfaces;

public enum PasswordChangeResult { Changed, UserNotFound, InvalidCurrentPassword, Conflict, PasswordNotSet }

public interface IUserPasswordService
{
    Task<PasswordChangeResult> ChangeAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken);
}
