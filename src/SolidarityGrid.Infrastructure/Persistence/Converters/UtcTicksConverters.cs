using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SolidarityGrid.Infrastructure.Persistence.Converters;

public sealed class UtcTicksConverter : ValueConverter<DateTimeOffset, long>
{
    public UtcTicksConverter()
        : base(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero))
    {
    }
}

public sealed class NullableUtcTicksConverter : ValueConverter<DateTimeOffset?, long?>
{
    public NullableUtcTicksConverter()
        : base(
            value => value.HasValue ? value.Value.UtcTicks : null,
            value => value.HasValue
                ? new DateTimeOffset(value.Value, TimeSpan.Zero)
                : null)
    {
    }
}
