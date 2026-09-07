using StockLab.Api.DTOs;
using StockLab.Application.Exceptions;

namespace StockLab.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
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
            if (!invalidRequest && !rateLimited)
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
            context.Response.StatusCode = rateLimited ? StatusCodes.Status429TooManyRequests : invalidRequest
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError;

            var error = rateLimited
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
