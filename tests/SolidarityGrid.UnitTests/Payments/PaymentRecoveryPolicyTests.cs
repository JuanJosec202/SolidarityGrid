using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Application.Payments.Recovery;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentRecoveryPolicyTests
{
    private static readonly DateTimeOffset Now =
        PaymentTestData.CreatedAt.AddMinutes(2);

    [Theory]
    [InlineData(PaymentStatus.Claimed)]
    [InlineData(PaymentStatus.Processing)]
    public void ExpiredPaymentOwnedByUnreachablePeerIsEligible(
        PaymentStatus status)
    {
        var payment = CreateOwnedPayment(status, Now.AddSeconds(-1));

        var decision = PaymentRecoveryPolicy.Evaluate(
            payment,
            PaymentTestData.NodeA,
            Snapshot(MeshPeerHealthStatus.Unreachable),
            Now);

        Assert.Equal(PaymentRecoveryDecision.Eligible, decision);
    }

    [Fact]
    public void ActiveLeaseIsNotEligible()
    {
        var payment = CreateOwnedPayment(
            PaymentStatus.Processing,
            Now.AddSeconds(1));

        var decision = PaymentRecoveryPolicy.Evaluate(
            payment,
            PaymentTestData.NodeA,
            Snapshot(MeshPeerHealthStatus.Unreachable),
            Now);

        Assert.Equal(PaymentRecoveryDecision.LeaseStillActive, decision);
    }

    [Theory]
    [InlineData(MeshPeerHealthStatus.Alive)]
    [InlineData(MeshPeerHealthStatus.Suspected)]
    [InlineData(MeshPeerHealthStatus.Unknown)]
    public void OwnerMustBeUnreachable(MeshPeerHealthStatus status)
    {
        var payment = CreateOwnedPayment(
            PaymentStatus.Processing,
            Now.AddSeconds(-1));

        var decision = PaymentRecoveryPolicy.Evaluate(
            payment,
            PaymentTestData.NodeA,
            Snapshot(status),
            Now);

        Assert.Equal(
            PaymentRecoveryDecision.OwnerNotUnreachable,
            decision);
    }

    [Fact]
    public void LocalOwnerIsNotEligible()
    {
        var payment = PaymentTestData.CreateProcessing(
            PaymentTestData.NodeA,
            leaseExpiresAt: Now.AddSeconds(-1));

        var decision = PaymentRecoveryPolicy.Evaluate(
            payment,
            PaymentTestData.NodeA,
            ownerHealth: null,
            Now);

        Assert.Equal(PaymentRecoveryDecision.OwnerIsLocal, decision);
    }

    [Theory]
    [InlineData(PaymentStatus.Replicated)]
    [InlineData(PaymentStatus.Completed)]
    public void InvalidStateIsNotEligible(PaymentStatus status)
    {
        var payment = status == PaymentStatus.Replicated
            ? PaymentTestData.CreateReplicated()
            : CreateCompleted();

        var decision = PaymentRecoveryPolicy.Evaluate(
            payment,
            PaymentTestData.NodeA,
            Snapshot(MeshPeerHealthStatus.Unreachable),
            Now);

        Assert.Equal(PaymentRecoveryDecision.InvalidState, decision);
    }

    [Fact]
    public void MissingOwnerIsNotEligible()
    {
        var payment = CreateOwnedPayment(
            PaymentStatus.Claimed,
            Now.AddSeconds(-1));
        typeof(Payment)
            .GetProperty(nameof(Payment.OwnerNodeId))!
            .SetValue(payment, null);

        var decision = PaymentRecoveryPolicy.Evaluate(
            payment,
            PaymentTestData.NodeA,
            ownerHealth: null,
            Now);

        Assert.Equal(PaymentRecoveryDecision.MissingOwner, decision);
    }

    [Fact]
    public void MissingSnapshotIsNotEligible()
    {
        var payment = CreateOwnedPayment(
            PaymentStatus.Processing,
            Now.AddSeconds(-1));

        var decision = PaymentRecoveryPolicy.Evaluate(
            payment,
            PaymentTestData.NodeA,
            ownerHealth: null,
            Now);

        Assert.Equal(
            PaymentRecoveryDecision.OwnerNotUnreachable,
            decision);
    }

    private static Payment CreateOwnedPayment(
        PaymentStatus status,
        DateTimeOffset leaseExpiresAtUtc)
    {
        var payment = PaymentTestData.CreateClaimed(
            PaymentTestData.NodeB,
            leaseExpiresAt: leaseExpiresAtUtc);
        if (status == PaymentStatus.Processing)
        {
            payment.StartProcessing(
                PaymentTestData.NodeB,
                1,
                PaymentTestData.CreatedAt.AddSeconds(3));
        }

        return payment;
    }

    private static Payment CreateCompleted()
    {
        var payment = PaymentTestData.CreateProcessing(
            PaymentTestData.NodeB,
            leaseExpiresAt: Now.AddSeconds(1));
        payment.Complete(
            PaymentTestData.NodeB,
            1,
            PaymentTestData.CreatedAt.AddSeconds(4));
        return payment;
    }

    private static MeshPeerHealthSnapshot Snapshot(
        MeshPeerHealthStatus status) =>
        new(
            PaymentTestData.NodeB,
            new Uri("http://node-b:8081"),
            status,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Now.AddMinutes(-1),
            Now,
            status == MeshPeerHealthStatus.Alive ? Now : null,
            status == MeshPeerHealthStatus.Alive ? null : Now,
            Now,
            status == MeshPeerHealthStatus.Alive ? 0 : 10,
            status == MeshPeerHealthStatus.Alive ? 1 : 0,
            status == MeshPeerHealthStatus.Alive ? 0 : 10,
            0,
            TimeSpan.FromMilliseconds(1),
            null,
            null,
            1);
}
