using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Infrastructure.Persistence.Converters;

public sealed class PaymentStatusConverter : ValueConverter<PaymentStatus, string>
{
    public PaymentStatusConverter()
        : base(
            status => ToToken(status),
            token => FromToken(token))
    {
    }

    public static string ToToken(PaymentStatus status) =>
        status switch
        {
            PaymentStatus.Received => "Received",
            PaymentStatus.Replicated => "Replicated",
            PaymentStatus.Claimed => "Claimed",
            PaymentStatus.Processing => "Processing",
            PaymentStatus.Completed => "Completed",
            _ => throw new InvalidOperationException($"Unknown payment status '{status}'."),
        };

    public static PaymentStatus FromToken(string token) =>
        token switch
        {
            "Received" => PaymentStatus.Received,
            "Replicated" => PaymentStatus.Replicated,
            "Claimed" => PaymentStatus.Claimed,
            "Processing" => PaymentStatus.Processing,
            "Completed" => PaymentStatus.Completed,
            _ => throw new InvalidOperationException(
                $"Unknown persisted payment status token '{token}'."),
        };
}
