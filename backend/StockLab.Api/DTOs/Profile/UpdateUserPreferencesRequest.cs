using System.Text.Json.Serialization;

namespace StockLab.Api.DTOs.Profile;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateUserPreferencesRequest(bool? EmailNotifications, bool? TradeAlerts,
    bool? MarketingCommunications, bool? DarkMode);
