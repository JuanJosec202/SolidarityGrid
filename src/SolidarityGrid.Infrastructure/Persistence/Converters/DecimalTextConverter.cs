using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SolidarityGrid.Infrastructure.Persistence.Converters;

public sealed class DecimalTextConverter : ValueConverter<decimal, string>
{
    public DecimalTextConverter()
        : base(
            value => ToInvariantText(value),
            value => FromInvariantText(value))
    {
    }

    private static string ToInvariantText(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);

    private static decimal FromInvariantText(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
