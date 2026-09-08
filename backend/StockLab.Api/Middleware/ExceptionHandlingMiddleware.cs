using StockLab.Api.DTOs;
using StockLab.Application.Exceptions;

namespace StockLab.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private static int ProviderStatus(MarketDataProviderFailure failure) => failure switch
    {
        MarketDataProviderFailure.RangeTooLarge => 400,
        MarketDataProviderFailure.Timeout => 504,
        MarketDataProviderFailure.InvalidRequest or MarketDataProviderFailure.MalformedResponse => 502,
        _ => 503
    };
    private static ApiErrorResponse ProviderError(MarketDataProviderFailure failure) => failure switch
    {
        MarketDataProviderFailure.RangeTooLarge => new("market_data_range_too_large", "Request a smaller history range."),
        MarketDataProviderFailure.Timeout => new("market_data_provider_timeout", "Market data provider timed out."),
        MarketDataProviderFailure.UpstreamRateLimited => new("market_data_provider_rate_limited", "Market data provider is temporarily rate limited."),
        MarketDataProviderFailure.InvalidRequest or MarketDataProviderFailure.MalformedResponse =>
            new("market_data_provider_invalid_response", "Market data provider could not complete the request."),
        _ => new("market_data_provider_unavailable", "Market data provider is temporarily unavailable.")
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client has disconnected. Do not write JSON to an aborted response.
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }
        }
        catch (Exception exception)
        {
            var invalidRequest = exception is ArgumentException or NotSupportedException;
            var rateLimited = exception is MarketDataRateLimitException;
            var providerFailure = exception as MarketDataProviderException;
            if (!invalidRequest && !rateLimited && providerFailure is null)
            {
                // Exception messages, stacks and request values may contain credentials.
                // Log only the category and correlation ID, not the exception object.
                logger.LogError("Unhandled exception {ExceptionType}. TraceId: {TraceId}",
                    exception.GetType().Name, context.TraceIdentifier);
            }

            // Headers/body already sent cannot safely be replaced by an error document.
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = providerFailure is not null ? ProviderStatus(providerFailure.Category) : rateLimited ? StatusCodes.Status429TooManyRequests : invalidRequest
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError;

            var error = providerFailure is not null ? ProviderError(providerFailure.Category) : rateLimited
                ? new ApiErrorResponse("market_data_rate_limited", "Market data requests are temporarily rate limited.")
                : exception is NotSupportedException
                ? new ApiErrorResponse("unsupported_operation", "The requested operation or interval is not supported.")
                : invalidRequest
                ? new ApiErrorResponse("invalid_request", "The request is invalid.")
                : new ApiErrorResponse("internal_server_error", "An unexpected error occurred.");
            await context.Response.WriteAsJsonAsync(error, context.RequestAborted);
        }
    }
}
