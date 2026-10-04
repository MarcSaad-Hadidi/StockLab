using System.ComponentModel.DataAnnotations;

namespace StockLab.Api.DTOs.Watchlist;

public sealed class AddWatchlistItemRequest
{
    private string symbol = string.Empty;

    [Required]
    [StringLength(32)]
    [RegularExpression(@"^(?!\.{1,2}$)[^/]+$")]
    public string Symbol
    {
        get => symbol;
        init => symbol = value?.Trim() ?? string.Empty;
    }
}
