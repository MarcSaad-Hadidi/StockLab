using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StockLab.Api.Middleware;

namespace StockLab.UnitTests.Api;

public sealed class ExceptionHandlingMiddlewareTests : IDisposable
{
    private readonly ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
    private readonly RecordingLogger logger = new();

    [Theory]
    [InlineData("argument")]
    [InlineData("range")]
    [InlineData("unsupported")]
    public async Task Invalid_request_returns_safe_400_json_without_error_logging(string kind)
    {
        Exception exception = kind switch
        {
            "argument" => new ArgumentException("private provider details"),
            "range" => new ArgumentOutOfRangeException("private parameter"),
            _ => new NotSupportedException("private implementation details")
        };
        var context = Context();

        await Middleware(exception).InvokeAsync(context);

        Assert.Equal(400, context.Response.StatusCode);
        if (kind == "unsupported")
            AssertError(context, "unsupported_operation", "The requested operation or interval is not supported.");
        else
            AssertError(context, "invalid_request", "The request is invalid.");
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task Unexpected_exception_returns_generic_500_and_logs_only_safe_metadata()
    {
        var context = Context();
        context.TraceIdentifier = "test-trace";
        var exception = new InvalidOperationException("sensitive-marker C:\\private\\file.cs",
            new Exception("sensitive-inner-marker"));

        await Middleware(exception).InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        AssertError(context, "internal_server_error", "An unexpected error occurred.");
        var entry = Assert.Single(logger.Messages);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("InvalidOperationException", entry.Message);
        Assert.Contains("test-trace", entry.Message);
        Assert.DoesNotContain("sensitive", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task Client_cancellation_returns_499_without_body_or_error_log()
    {
        using var cancellation = new CancellationTokenSource();
        var context = Context();
        context.RequestAborted = cancellation.Token;
        var middleware = new ExceptionHandlingMiddleware(_ =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, logger);

        await middleware.InvokeAsync(context);

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task Cancellation_without_client_abort_is_not_silently_treated_as_disconnect()
    {
        var context = Context();
        await Middleware(new OperationCanceledException("internal cancellation")).InvokeAsync(context);
        Assert.Equal(500, context.Response.StatusCode);
        AssertError(context, "internal_server_error", "An unexpected error occurred.");
    }

    [Fact]
    public async Task Started_response_is_not_replaced_with_a_second_document()
    {
        var context = Context();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        var exception = new InvalidOperationException("already streaming");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Middleware(exception).InvokeAsync(context));

        Assert.Same(exception, actual);
        Assert.Equal(200, context.Response.StatusCode);
    }

    private ExceptionHandlingMiddleware Middleware(Exception exception) =>
        new(_ => Task.FromException(exception), logger);

    private DefaultHttpContext Context() => new()
    {
        RequestServices = services,
        Response = { Body = new MemoryStream() }
    };

    private static void AssertError(HttpContext context, string error, string message)
    {
        Assert.StartsWith("application/json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var json = JsonDocument.Parse(context.Response.Body);
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        Assert.Equal(error, json.RootElement.GetProperty("error").GetString());
        Assert.Equal(message, json.RootElement.GetProperty("message").GetString());
    }

    public void Dispose() => services.Dispose();

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class RecordingLogger : ILogger<ExceptionHandlingMiddleware>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add((logLevel, formatter(state, exception), exception));
    }
}
