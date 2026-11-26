using System.Threading.Channels;
using Microsoft.Extensions.Options;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Identity;

public sealed record PasswordRecoveryRequest(string Email, string Language);

public sealed class PasswordRecoveryRequestQueue(IOptions<PasswordRecoveryOptions> options,
    IPasswordRecoveryEmailSender sender) : IPasswordRecoveryRequestQueue
{
    private readonly Channel<PasswordRecoveryRequest> channel = Channel.CreateBounded<PasswordRecoveryRequest>(
        new BoundedChannelOptions(100) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    public async Task<bool> TryEnqueueAsync(string email, string language, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !await sender.IsAvailableAsync(cancellationToken)) return false;
        cancellationToken.ThrowIfCancellationRequested();
        return channel.Writer.TryWrite(new(email, language));
    }
    public IAsyncEnumerable<PasswordRecoveryRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}
