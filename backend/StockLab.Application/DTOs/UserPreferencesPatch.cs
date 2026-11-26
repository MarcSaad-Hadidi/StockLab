namespace StockLab.Application.DTOs;

public sealed record UserPreferencesPatch(bool? EmailNotifications, bool? TradeAlerts,
    bool? MarketingCommunications, bool? DarkMode)
{
    public bool HasChanges => EmailNotifications.HasValue || TradeAlerts.HasValue
        || MarketingCommunications.HasValue || DarkMode.HasValue;
}
