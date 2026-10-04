using System.ComponentModel.DataAnnotations;

namespace StockLab.Api.DTOs.Watchlist;

public sealed class AddWatchlistItemRequest
{
    private string symbol = string.Empty;

    [Required]
    [StringLength(32)]
    [RegularExpression(@"^(?!\.{1,2}$)[^/\x00-\x1F\x7F-\x9F]+$")]
    public string Symbol
    {
        get => symbol;
        init => symbol = value?.Trim() ?? string.Empty;
    }
}
