using System.Numerics;
using StockLab.Application.DTOs.Portfolio;

namespace StockLab.Infrastructure.Portfolios;

/// <summary>Reject observations whose decimal arithmetic has already lost meaningful digits.</summary>
internal static class PortfolioValuationPrecision
{
    public static bool IsExact(PortfolioPerformance value)
    {
        var positions = BigInteger.Zero;
        foreach (var position in value.Positions)
        {
            if (position.Quantity <= 0m || position.CurrentPrice <= 0m) return false;
            var (quantity, quantityScale) = Parts(position.Quantity);
            var (price, priceScale) = Parts(position.CurrentPrice);
            var product = quantity * price;
            var shift = quantityScale + priceScale - 12;
            BigInteger rounded;
            if (shift <= 0) rounded = product * BigInteger.Pow(10, -shift);
            else
            {
                var divisor = BigInteger.Pow(10, shift);
                rounded = BigInteger.DivRem(product, divisor, out var remainder);
                if (remainder * 2 >= divisor) rounded++;
            }
            if (rounded != Units(position.MarketValue)) return false;
            positions += rounded;
        }
        return positions == Units(value.PositionsMarketValue)
            && positions + Units(value.CashBalance) == Units(value.TotalValue);
    }

    private static BigInteger Units(decimal value)
    {
        var (coefficient, scale) = Parts(value);
        return scale <= 12 ? coefficient * BigInteger.Pow(10, 12 - scale)
            : coefficient / BigInteger.Pow(10, scale - 12);
    }

    private static (BigInteger Coefficient, int Scale) Parts(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
        return ((bits[3] & int.MinValue) == 0 ? coefficient : -coefficient, (bits[3] >> 16) & 0xff);
    }
}
