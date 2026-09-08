using Microsoft.AspNetCore.Mvc.ModelBinding;
using StockLab.Application.DTOs.MarketData;

namespace StockLab.Api.Validation;

/// <summary>Preserves single interval tokens and explicit UTC history bounds during binding.</summary>
public sealed class HistoryParameterModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None)
            return Task.CompletedTask; // BindRequired handles missing parameters.

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
        var token = value.FirstValue?.Trim();
        object? result = null;
        if (value.Length == 1 && !string.IsNullOrEmpty(token))
        {
            if (bindingContext.ModelType == typeof(StockHistoryInterval?)
                && !token.Contains(',')
                && Enum.TryParse<StockHistoryInterval>(token, true, out var interval)
                && Enum.IsDefined(interval))
            {
                result = interval;
            }
            else if (bindingContext.ModelType == typeof(string))
            {
                result = token;
            }
        }

        if (result is null)
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "The value is missing or invalid.");
        else
            bindingContext.Result = ModelBindingResult.Success(result);

        return Task.CompletedTask;
    }
}
