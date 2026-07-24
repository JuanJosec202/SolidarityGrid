using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentCoordinationReceiverTests
{
    private static readonly DateTimeOffset OccurredAt =
        PaymentTestData.CreatedAt.AddSeconds(2);
    private static readonly DateTimeOffset LeaseExpiresAt =
        PaymentTestData.CreatedAt.AddMinutes(1);

    [Fact]
    public async Task ValidClaimIsPersisted()
    {
        var payment = PaymentTestData.CreateReplicated();
        var fixture = new Fixture(payment);

        var result = await fixture.Claim.ExecuteAsync(
            ClaimRequest(),
            CancellationToken.None);

        Assert.True(result.Granted);
        Assert.False(result.AlreadyApplied);
        Assert.Equal(PaymentStatus.Claimed, payment.Status);
        Assert.Equal(PaymentTestData.NodeA, payment.OwnerNodeId);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task RepeatedClaimIsIdempotent()
    {
        var payment = PaymentTestData.CreateReplicated();
        var fixture = new Fixture(payment);
        var request = ClaimRequest();
        await fixture.Claim.ExecuteAsync(request, CancellationToken.None);

        var result = await fixture.Claim.ExecuteAsync(
            request,
            CancellationToken.None);

        Assert.True(result.Granted);
        Assert.True(result.AlreadyApplied);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task ActiveLeaseRejectsDifferentOwner()
    {
        var payment = PaymentTestData.CreateClaimed();
        var fixture = new Fixture(payment);

        var result = await fixture.Claim.ExecuteAsync(
            ClaimRequest(PaymentTestData.NodeB, 2),
            CancellationToken.None);

        Assert.False(result.Granted);
        Assert.Equal(
            PaymentCoordinationErrorCodes.ClaimLeaseActive,
            result.ErrorCode);
        Assert.Equal(PaymentTestData.NodeA, payment.OwnerNodeId);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task StaleTermIsRejected()
    {
        var payment = PaymentTestData.CreateReplicated();
        var fixture = new Fixture(payment);

        var result = await fixture.Claim.ExecuteAsync(
            ClaimRequest(proposedTerm: 0),
            CancellationToken.None);

        Assert.False(result.Granted);
        Assert.Equal(
            PaymentCoordinationErrorCodes.ClaimTermStale,
            result.ErrorCode);
    }

    [Fact]
    public async Task ValidStartIsPersisted()
    {
        var payment = PaymentTestData.CreateClaimed();
        var fixture = new Fixture(payment);

        var result = await fixture.Start.ExecuteAsync(
            Command(OccurredAt.AddSeconds(1)),
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.False(result.AlreadyApplied);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(1, payment.Attempt);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task RepeatedStartIsIdempotent()
    {
        var payment = PaymentTestData.CreateProcessing();
        var fixture = new Fixture(payment);

        var result = await fixture.Start.ExecuteAsync(
            Command(OccurredAt.AddSeconds(2)),
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.True(result.AlreadyApplied);
        Assert.Equal(1, payment.Attempt);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task ValidLeaseRenewalIsPersisted()
    {
        var payment = PaymentTestData.CreateProcessing();
        var fixture = new Fixture(payment);
        var renewedUntil = LeaseExpiresAt.AddMinutes(1);

        var result = await fixture.Renew.ExecuteAsync(
            Command(OccurredAt.AddSeconds(2), renewedUntil),
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal(renewedUntil, payment.LeaseExpiresAtUtc);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task LeaseRenewalFromDifferentOwnerIsRejected()
    {
        var payment = PaymentTestData.CreateProcessing();
        var fixture = new Fixture(payment);

        var result = await fixture.Renew.ExecuteAsync(
            Command(
                OccurredAt.AddSeconds(2),
                LeaseExpiresAt.AddMinutes(1),
                PaymentTestData.NodeB),
            CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal(PaymentCoordinationErrorCodes.OwnerMismatch, result.ErrorCode);
    }

    [Fact]
    public async Task ValidCompletionIsPersisted()
    {
        var payment = PaymentTestData.CreateProcessing();
        var fixture = new Fixture(payment);

        var result = await fixture.Complete.ExecuteAsync(
            Command(OccurredAt.AddSeconds(2)),
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.False(result.AlreadyApplied);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task RepeatedCompletionIsIdempotent()
    {
        var payment = PaymentTestData.CreateProcessing();
        payment.Complete(
            PaymentTestData.NodeA,
            1,
            OccurredAt.AddSeconds(2));
        var fixture = new Fixture(payment);

        var result = await fixture.Complete.ExecuteAsync(
            Command(OccurredAt.AddSeconds(3)),
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.True(result.AlreadyApplied);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    private static PaymentClaimRequest ClaimRequest(
        NodeId? candidate = null,
        long proposedTerm = 1) =>
        new(
            PaymentTestData.PaymentId,
            candidate ?? PaymentTestData.NodeA,
            proposedTerm,
            LeaseExpiresAt,
            OccurredAt);

    private static PaymentCoordinationCommand Command(
        DateTimeOffset occurredAtUtc,
        DateTimeOffset? leaseExpiresAtUtc = null,
        NodeId? owner = null) =>
        new(
            PaymentTestData.PaymentId,
            owner ?? PaymentTestData.NodeA,
            1,
            occurredAtUtc,
            leaseExpiresAtUtc);

    private sealed class Fixture
    {
        public Fixture(Payment payment)
        {
            var repository = new FakeRepository(payment);
            UnitOfWork = new FakeUnitOfWork();
            Claim = new ReceivePaymentClaimUseCase(repository, UnitOfWork);
            Start = new ReceivePaymentProcessingStartedUseCase(
                repository,
                UnitOfWork);
            Renew = new ReceivePaymentLeaseRenewalUseCase(
                repository,
                UnitOfWork);
            Complete = new ReceivePaymentCompletionUseCase(
                repository,
                UnitOfWork);
        }

        public FakeUnitOfWork UnitOfWork { get; }

        public ReceivePaymentClaimUseCase Claim { get; }

        public ReceivePaymentProcessingStartedUseCase Start { get; }

        public ReceivePaymentLeaseRenewalUseCase Renew { get; }

        public ReceivePaymentCompletionUseCase Complete { get; }
    }

    private sealed class FakeRepository(Payment payment) : IPaymentRepository
    {
        public Task<Payment?> GetByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(payment.Id == paymentId ? payment : null);
        }

        public Task<Payment?> GetByIdempotencyKeyAsync(
            IdempotencyKey idempotencyKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<Payment>> GetReplicatedPaymentsAsync(
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<Payment>> GetRecoverablePaymentsAsync(
            DateTimeOffset utcNow,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask AddAsync(
            Payment paymentToAdd,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            return Task.FromResult(1);
        }
    }

}
