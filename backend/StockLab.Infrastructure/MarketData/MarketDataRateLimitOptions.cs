using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace StockLab.Infrastructure.MarketData;

public sealed class MarketDataRateLimitOptions
{
    public const string SectionName = "MarketDataRateLimit";
    public const string ValidationMessage = "PermitLimit must be 1..10000, QueueLimit 0..1000, and Window 1 millisecond..1 day.";
    // StockLab local defaults; these do not describe any external provider plan.
    public int PermitLimit { get; set; } = 30;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
    public int QueueLimit { get; set; } = 10;

    public bool IsValid() => PermitLimit is >= 1 and <= 10000
        && QueueLimit is >= 0 and <= 1000
        && Window >= TimeSpan.FromMilliseconds(1) && Window <= TimeSpan.FromDays(1);

    public FixedWindowRateLimiter CreateLimiter()
    {
        if (!IsValid())
            throw new OptionsValidationException(SectionName, typeof(MarketDataRateLimitOptions), [ValidationMessage]);
        return new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitLimit, Window = Window, QueueLimit = QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst, AutoReplenishment = true
        });
    }
}
