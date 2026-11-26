using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Identity;

public sealed class PasswordRecoveryRequestProcessor(StockLabDbContext dbContext,
    IPasswordRecoveryEmailSender sender, IOptions<PasswordRecoveryOptions> options, TimeProvider timeProvider)
{
    public async Task ProcessAsync(PasswordRecoveryRequest request, CancellationToken cancellationToken)
    {
        var normalized = EmailAddressNormalizer.Normalize(request.Email);
        var user = await dbContext.Users.SingleOrDefaultAsync(value => value.NormalizedEmail == normalized, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (user is null || user.PasswordResetRequestedAtUtc > now.AddSeconds(-60)) return;
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        user.PasswordResetTokenHash = SHA256.HashData(Encoding.ASCII.GetBytes(token));
        user.PasswordResetExpiresAtUtc = now.AddMinutes(options.Value.TokenLifetimeMinutes);
        user.PasswordResetRequestedAtUtc = now;
        try
        {
            // Persist before delivery; a profile/password race must not mail a token for overwritten state.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return; }
        await sender.SendAsync(user.Email, request.Language, user.Id, token, cancellationToken);
    }
}
