using System.Threading.Channels;
using Microsoft.Extensions.Options;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Identity;

public sealed record PasswordRecoveryRequest(string Email, string Language);

public sealed class PasswordRecoveryRequestQueue(IOptions<PasswordRecoveryOptions> options) : IPasswordRecoveryRequestQueue
{
    private readonly Channel<PasswordRecoveryRequest> channel = Channel.CreateBounded<PasswordRecoveryRequest>(
        new BoundedChannelOptions(100) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    public bool TryEnqueue(string email, string language) => options.Value.Enabled &&
        channel.Writer.TryWrite(new(email, language));
    public IAsyncEnumerable<PasswordRecoveryRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}
