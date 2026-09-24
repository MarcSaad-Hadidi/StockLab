using System.Globalization;

namespace StockLab.Infrastructure.Identity;

internal static class EmailAddressNormalizer
{
    public static string Normalize(string email) => email.Trim().ToUpper(CultureInfo.InvariantCulture);
}
