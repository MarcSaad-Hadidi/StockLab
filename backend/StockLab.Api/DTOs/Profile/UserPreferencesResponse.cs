namespace StockLab.Api.DTOs.Profile;

public sealed record UserPreferencesResponse(Guid UserId, bool EmailNotifications, bool TradeAlerts,
    bool MarketingCommunications, bool DarkMode);
