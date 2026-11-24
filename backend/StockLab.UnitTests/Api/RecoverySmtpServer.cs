using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using MimeKit;

namespace StockLab.UnitTests.Api;

// A local SMTP peer that captures real MIME messages without relaying them.
internal sealed class RecoverySmtpServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly Channel<MimeMessage> messages = Channel.CreateUnbounded<MimeMessage>();
    private readonly Task serve;
    private int connections;
    private int recipients;
    public volatile bool AcceptConnections = true;
    public volatile bool AcceptSender = true;
    public bool RequireAuthentication { get; init; }
    public volatile bool AcceptAuthentication = true;
    public int ConnectionCount => Volatile.Read(ref connections);
    public int RecipientCount => Volatile.Read(ref recipients);
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public RecoverySmtpServer()
    {
        listener.Start();
        serve = ServeAsync(stopping.Token);
    }

    public Task<MimeMessage> NextMessageAsync() => messages.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

    private async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            Interlocked.Increment(ref connections);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            var available = AcceptConnections;
            await Reply(available ? "220 stocklab.test ESMTP" : "421 private SMTP reply");
            if (!available) continue;
            var authenticated = !RequireAuthentication;

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                var verb = line.Split(' ', 2)[0];
                switch (verb)
                {
                    case "EHLO":
                    case "HELO":
                        await Reply(RequireAuthentication ? "250-stocklab.test\r\n250 AUTH PLAIN" : "250 stocklab.test");
                        break;
                    case "AUTH":
                        var credentials = line.Split(' ', 3).Last();
                        var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes("\0test-user\0private-test-password"));
                        authenticated = AcceptAuthentication && credentials == expected;
                        await Reply(authenticated ? "235 Authenticated" : "535 private SMTP reply");
                        break;
                    case "MAIL":
                        await Reply(!authenticated ? "530 Authentication required" :
                            AcceptSender ? "250 Sender accepted" : "550 private SMTP reply");
                        break;
                    case "RCPT":
                        Interlocked.Increment(ref recipients);
                        await Reply("250 Recipient accepted");
                        break;
                    case "DATA":
                        await Reply("354 End with a dot");
                        using (var content = new MemoryStream())
                        {
                            while (await reader.ReadLineAsync(ct) is { } data && data != ".")
                            {
                                if (data.StartsWith("..", StringComparison.Ordinal)) data = data[1..];
                                await content.WriteAsync(Encoding.ASCII.GetBytes(data + "\r\n"), ct);
                            }
                            content.Position = 0;
                            var message = await MimeMessage.LoadAsync(content, ct);
                            await Reply("250 Message accepted");
                            await messages.Writer.WriteAsync(message, ct);
                        }
                        break;
                    case "RSET":
                        await Reply("250 Reset");
                        break;
                    case "QUIT":
                        await Reply("221 Closing connection");
                        break;
                    default:
                        await Reply("500 Unsupported command");
                        break;
                }
                if (verb == "QUIT") break;
            }

            Task Reply(string reply) => writer.WriteLineAsync(reply.AsMemory(), ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        try { await serve; }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally
        {
            listener.Stop();
            stopping.Dispose();
        }
    }
}
