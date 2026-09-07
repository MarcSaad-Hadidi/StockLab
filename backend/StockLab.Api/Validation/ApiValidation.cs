using Microsoft.AspNetCore.Mvc;
using StockLab.Api.DTOs;

namespace StockLab.Api.Validation;

public static class ApiValidation
{
    public static void Configure(ApiBehaviorOptions options)
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            // Never echo attempted values, binder exceptions or technical messages.
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => entry.Key,
                    _ => new[] { "The value is missing or invalid." });

            return new BadRequestObjectResult(new ApiValidationErrorResponse(
                "validation_error", "The request contains invalid data.", errors));
        };
    }
}
