using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Domain.Payments.Events;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentDomainEventsTests
{
    [Fact]
    public void LifecycleEventsAreOrderedAndVersioned()
    {
        var payment = PaymentTestData.CreateReceived();
        payment.MarkReplicated(PaymentTestData.CreatedAt.AddSeconds(1));
        payment.Claim(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddMinutes(1),
            PaymentTestData.CreatedAt.AddSeconds(2));
        payment.StartProcessing(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(3));
        payment.RenewLease(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddMinutes(2),
            PaymentTestData.CreatedAt.AddSeconds(4));
        payment.Complete(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(5));

        var events = payment.DequeueDomainEvents().ToArray();

        Assert.Collection(
            events,
            item => Assert.IsType<PaymentCreatedDomainEvent>(item),
            item => Assert.IsType<PaymentReplicatedDomainEvent>(item),
            item => Assert.IsType<PaymentClaimedDomainEvent>(item),
            item => Assert.IsType<PaymentProcessingStartedDomainEvent>(item),
            item => Assert.IsType<PaymentLeaseRenewedDomainEvent>(item),
            item => Assert.IsType<PaymentCompletedDomainEvent>(item));
        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L], events.Select(item => item.Version));
    }

    [Fact]
    public void EventTimesAreStoredInUtc()
    {
        var localTime = new DateTimeOffset(2026, 7, 23, 7, 0, 0, TimeSpan.FromHours(-5));
        var payment = PaymentTestData.CreateReceived(createdAt: localTime);
        payment.MarkReplicated(localTime.AddMinutes(1));

        var events = payment.DequeueDomainEvents();

        Assert.All(events, domainEvent => Assert.Equal(TimeSpan.Zero, domainEvent.OccurredAtUtc.Offset));
    }

    [Fact]
    public void FirstClaimEventIsNotTakeover()
    {
        var payment = PaymentTestData.CreateClaimed();

        var claimed = Assert.IsType<PaymentClaimedDomainEvent>(
            payment.DequeueDomainEvents().Last());

        Assert.False(claimed.IsTakeover);
        Assert.Equal(PaymentTestData.NodeA, claimed.OwnerNodeId);
        Assert.Equal(1, claimed.Term);
        Assert.Equal(PaymentTestData.CreatedAt.AddMinutes(1), claimed.LeaseExpiresAtUtc);
    }

    [Fact]
    public void TakeoverEventContainsNewOwnerAndTerm()
    {
        var payment = PaymentTestData.CreateProcessing();
        var takeoverAt = PaymentTestData.CreatedAt.AddMinutes(1);

        payment.Claim(
            PaymentTestData.NodeB,
            2,
            takeoverAt.AddMinutes(1),
            takeoverAt);

        var claimed = Assert.IsType<PaymentClaimedDomainEvent>(
            payment.DequeueDomainEvents().Last());
        Assert.True(claimed.IsTakeover);
        Assert.Equal(PaymentTestData.NodeB, claimed.OwnerNodeId);
        Assert.Equal(2, claimed.Term);
    }

    [Fact]
    public void ProcessingEventContainsAttempt()
    {
        var payment = PaymentTestData.CreateClaimed();

        payment.StartProcessing(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(3));

        var processing = Assert.IsType<PaymentProcessingStartedDomainEvent>(
            payment.DequeueDomainEvents().Last());
        Assert.Equal(PaymentTestData.NodeA, processing.OwnerNodeId);
        Assert.Equal(1, processing.Term);
        Assert.Equal(1, processing.Attempt);
    }

    [Fact]
    public void NoOpDoesNotAppendEvent()
    {
        var payment = PaymentTestData.CreateReplicated();
        payment.DequeueDomainEvents();

        payment.MarkReplicated(PaymentTestData.CreatedAt.AddSeconds(10));

        Assert.Empty(payment.DequeueDomainEvents());
    }

    [Fact]
    public void DequeueReturnsIndependentSnapshots()
    {
        var payment = PaymentTestData.CreateReceived();
        var firstSnapshot = payment.DequeueDomainEvents();
        payment.MarkReplicated(PaymentTestData.CreatedAt.AddSeconds(1));
        var secondSnapshot = payment.DequeueDomainEvents();

        Assert.IsType<PaymentCreatedDomainEvent>(Assert.Single(firstSnapshot));
        Assert.IsType<PaymentReplicatedDomainEvent>(Assert.Single(secondSnapshot));
    }
}
