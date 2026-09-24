using System.Threading.RateLimiting;

namespace StockLab.Api.Configuration;

public sealed class LoginRateLimitOptions
{
    public const string SectionName = "LoginRateLimit";
    public const string PolicyName = "LoginPerIp";
    public const string ValidationMessage = "PermitLimit must be 1..1000 and Window 1 millisecond..1 day.";

    public int PermitLimit { get; set; } = 5;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    public bool IsValid() => PermitLimit is >= 1 and <= 1000
        && Window >= TimeSpan.FromMilliseconds(1) && Window <= TimeSpan.FromDays(1);

    public FixedWindowRateLimiterOptions CreateLimiterOptions() => new()
    {
        PermitLimit = PermitLimit,
        Window = Window,
        QueueLimit = 0,
        AutoReplenishment = true
    };
}
