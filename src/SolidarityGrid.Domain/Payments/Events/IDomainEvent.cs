namespace SolidarityGrid.Domain.Payments.Events;

public interface IDomainEvent
{
    Guid PaymentId { get; }

    DateTimeOffset OccurredAtUtc { get; }

    long Version { get; }
}
