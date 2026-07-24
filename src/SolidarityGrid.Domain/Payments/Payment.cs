using SolidarityGrid.Domain.Payments.Events;

namespace SolidarityGrid.Domain.Payments;

public sealed class Payment
{
    private readonly List<IDomainEvent> _domainEvents = [];

    // Used only by persistence materializers. It deliberately emits no events.
    private Payment()
    {
        IdempotencyKey = null!;
        Amount = null!;
    }

    private Payment(
        Guid id,
        IdempotencyKey idempotencyKey,
        Money amount,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = PaymentStatus.Received;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Version = 1;

        _domainEvents.Add(new PaymentCreatedDomainEvent(
            Id,
            createdAtUtc,
            Version,
            IdempotencyKey,
            Amount));
    }

    public Guid Id { get; }

    public IdempotencyKey IdempotencyKey { get; }

    public Money Amount { get; }

    public PaymentStatus Status { get; private set; }

    public NodeId? OwnerNodeId { get; private set; }

    public long Term { get; private set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }

    public int Attempt { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public bool IsTerminal => Status == PaymentStatus.Completed;

    public static Payment Create(
        Guid id,
        IdempotencyKey? idempotencyKey,
        Money? amount,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new PaymentDomainException(
                PaymentErrorCodes.PaymentIdRequired,
                "The payment ID is required.");
        }

        if (idempotencyKey is null)
        {
            throw new PaymentDomainException(
                PaymentErrorCodes.IdempotencyKeyRequired,
                "The idempotency key is required.");
        }

        if (amount is null)
        {
            throw new PaymentDomainException(
                PaymentErrorCodes.PaymentAmountRequired,
                "The payment amount is required.");
        }

        return new Payment(id, idempotencyKey, amount, createdAtUtc.ToUniversalTime());
    }

    public bool MarkReplicated(DateTimeOffset occurredAtUtc)
    {
        if (Status != PaymentStatus.Received)
        {
            return false;
        }

        var occurredAt = occurredAtUtc.ToUniversalTime();
        ApplyTransition(PaymentStatus.Replicated, occurredAt);
        _domainEvents.Add(new PaymentReplicatedDomainEvent(Id, occurredAt, Version));
        return true;
    }

    public bool Claim(
        NodeId? candidateNodeId,
        long proposedTerm,
        DateTimeOffset leaseExpiresAtUtc,
        DateTimeOffset occurredAtUtc)
    {
        EnsureNodeId(candidateNodeId);

        var leaseExpiresAt = leaseExpiresAtUtc.ToUniversalTime();
        var occurredAt = occurredAtUtc.ToUniversalTime();

        if (IsExactClaimReplay(candidateNodeId!, proposedTerm, leaseExpiresAt))
        {
            return false;
        }

        if (Status == PaymentStatus.Received)
        {
            throw DomainError(
                PaymentErrorCodes.PaymentNotReplicated,
                "A payment must be replicated before it can be claimed.");
        }

        EnsureNotCompleted();
        EnsurePositiveTerm(proposedTerm);
        EnsureFutureLease(leaseExpiresAt, occurredAt);

        var isTakeover = Status is PaymentStatus.Claimed or PaymentStatus.Processing;
        if (isTakeover)
        {
            if (HasActiveLease(occurredAt))
            {
                throw DomainError(
                    PaymentErrorCodes.PaymentLeaseActive,
                    "The payment has an active lease.");
            }

            EnsureHigherTerm(proposedTerm);
        }
        else if (Status == PaymentStatus.Replicated)
        {
            EnsureHigherTerm(proposedTerm);
        }
        else
        {
            throw InvalidState("claim");
        }

        OwnerNodeId = candidateNodeId;
        Term = proposedTerm;
        LeaseExpiresAtUtc = leaseExpiresAt;
        ApplyTransition(PaymentStatus.Claimed, occurredAt);
        _domainEvents.Add(new PaymentClaimedDomainEvent(
            Id,
            occurredAt,
            Version,
            candidateNodeId!,
            Term,
            leaseExpiresAt,
            isTakeover));
        return true;
    }

    public bool StartProcessing(
        NodeId? ownerNodeId,
        long term,
        DateTimeOffset occurredAtUtc)
    {
        EnsureNodeId(ownerNodeId);

        if (Status == PaymentStatus.Processing)
        {
            EnsureOwnerAndTerm(ownerNodeId!, term);
            return false;
        }

        EnsureNotCompleted();
        if (Status != PaymentStatus.Claimed)
        {
            throw InvalidState("start processing");
        }

        EnsureOwnerAndTerm(ownerNodeId!, term);
        var occurredAt = occurredAtUtc.ToUniversalTime();
        EnsureActiveLease(occurredAt);

        Attempt++;
        ApplyTransition(PaymentStatus.Processing, occurredAt);
        _domainEvents.Add(new PaymentProcessingStartedDomainEvent(
            Id,
            occurredAt,
            Version,
            ownerNodeId!,
            Term,
            Attempt));
        return true;
    }

    public bool RenewLease(
        NodeId? ownerNodeId,
        long term,
        DateTimeOffset newLeaseExpiresAtUtc,
        DateTimeOffset occurredAtUtc)
    {
        EnsureNodeId(ownerNodeId);
        EnsureNotCompleted();

        if (Status is not (PaymentStatus.Claimed or PaymentStatus.Processing))
        {
            throw InvalidState("renew the lease");
        }

        EnsureOwnerAndTerm(ownerNodeId!, term);
        var occurredAt = occurredAtUtc.ToUniversalTime();
        EnsureActiveLease(occurredAt);

        var newLeaseExpiresAt = newLeaseExpiresAtUtc.ToUniversalTime();
        if (newLeaseExpiresAt == LeaseExpiresAtUtc)
        {
            return false;
        }

        if (newLeaseExpiresAt <= occurredAt || newLeaseExpiresAt < LeaseExpiresAtUtc)
        {
            throw DomainError(
                PaymentErrorCodes.PaymentLeaseMustIncrease,
                "The new lease expiration must be later than the current expiration.");
        }

        LeaseExpiresAtUtc = newLeaseExpiresAt;
        IncrementVersion(occurredAt);
        _domainEvents.Add(new PaymentLeaseRenewedDomainEvent(
            Id,
            occurredAt,
            Version,
            ownerNodeId!,
            Term,
            newLeaseExpiresAt));
        return true;
    }

    public bool Complete(
        NodeId? ownerNodeId,
        long term,
        DateTimeOffset occurredAtUtc)
    {
        if (Status == PaymentStatus.Completed)
        {
            EnsureNodeId(ownerNodeId);
            EnsureOwnerAndTerm(ownerNodeId!, term);
            return false;
        }

        EnsureNodeId(ownerNodeId);
        if (Status != PaymentStatus.Processing)
        {
            throw InvalidState("complete");
        }

        EnsureOwnerAndTerm(ownerNodeId!, term);
        var occurredAt = occurredAtUtc.ToUniversalTime();
        EnsureActiveLease(occurredAt);

        Status = PaymentStatus.Completed;
        CompletedAtUtc = occurredAt;
        LeaseExpiresAtUtc = null;
        IncrementVersion(occurredAt);
        _domainEvents.Add(new PaymentCompletedDomainEvent(
            Id,
            occurredAt,
            Version,
            ownerNodeId!,
            Term,
            Attempt));
        return true;
    }

    public bool HasActiveLease(DateTimeOffset utcNow) =>
        Status is PaymentStatus.Claimed or PaymentStatus.Processing &&
        LeaseExpiresAtUtc is { } leaseExpiresAt &&
        leaseExpiresAt > utcNow.ToUniversalTime();

    public bool IsLeaseExpired(DateTimeOffset utcNow) =>
        Status is PaymentStatus.Claimed or PaymentStatus.Processing &&
        LeaseExpiresAtUtc is { } leaseExpiresAt &&
        leaseExpiresAt <= utcNow.ToUniversalTime();

    public bool CanBeTakenOver(DateTimeOffset utcNow) =>
        Status is PaymentStatus.Claimed or PaymentStatus.Processing &&
        IsLeaseExpired(utcNow);

    public bool MatchesRequest(IdempotencyKey? key, Money? amount) =>
        key is not null &&
        amount is not null &&
        IdempotencyKey == key &&
        Amount == amount;

    public IReadOnlyCollection<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return Array.AsReadOnly(events);
    }

    private bool IsExactClaimReplay(
        NodeId candidateNodeId,
        long proposedTerm,
        DateTimeOffset leaseExpiresAtUtc) =>
        Status == PaymentStatus.Claimed &&
        OwnerNodeId == candidateNodeId &&
        Term == proposedTerm &&
        LeaseExpiresAtUtc == leaseExpiresAtUtc;

    private void ApplyTransition(PaymentStatus newStatus, DateTimeOffset occurredAtUtc)
    {
        Status = newStatus;
        IncrementVersion(occurredAtUtc);
    }

    private void IncrementVersion(DateTimeOffset occurredAtUtc)
    {
        Version++;
        UpdatedAtUtc = occurredAtUtc;
    }

    private void EnsureHigherTerm(long proposedTerm)
    {
        if (proposedTerm <= Term)
        {
            throw DomainError(
                PaymentErrorCodes.PaymentTermStale,
                "The proposed term must be greater than the current term.");
        }
    }

    private static void EnsurePositiveTerm(long proposedTerm)
    {
        if (proposedTerm <= 0)
        {
            throw DomainError(
                PaymentErrorCodes.PaymentTermStale,
                "The proposed term must be greater than zero.");
        }
    }

    private static void EnsureFutureLease(
        DateTimeOffset leaseExpiresAtUtc,
        DateTimeOffset occurredAtUtc)
    {
        if (leaseExpiresAtUtc <= occurredAtUtc)
        {
            throw DomainError(
                PaymentErrorCodes.PaymentLeaseInvalid,
                "The lease expiration must be later than the occurrence time.");
        }
    }

    private void EnsureActiveLease(DateTimeOffset occurredAtUtc)
    {
        if (!HasActiveLease(occurredAtUtc))
        {
            throw DomainError(
                PaymentErrorCodes.PaymentLeaseExpired,
                "The payment lease has expired.");
        }
    }

    private void EnsureOwnerAndTerm(NodeId ownerNodeId, long term)
    {
        if (OwnerNodeId != ownerNodeId)
        {
            throw DomainError(
                PaymentErrorCodes.PaymentOwnerMismatch,
                "The supplied node does not own the payment.");
        }

        if (Term != term)
        {
            throw DomainError(
                PaymentErrorCodes.PaymentTermMismatch,
                "The supplied term does not match the payment term.");
        }
    }

    private void EnsureNotCompleted()
    {
        if (Status == PaymentStatus.Completed)
        {
            throw DomainError(
                PaymentErrorCodes.PaymentAlreadyCompleted,
                "The payment is already completed.");
        }
    }

    private static void EnsureNodeId(NodeId? nodeId)
    {
        if (nodeId is null)
        {
            throw DomainError(
                PaymentErrorCodes.NodeIdInvalid,
                "A node ID is required.");
        }
    }

    private PaymentDomainException InvalidState(string operation) =>
        DomainError(
            PaymentErrorCodes.PaymentInvalidState,
            $"Cannot {operation} a payment in state {Status}.");

    private static PaymentDomainException DomainError(string code, string message) =>
        new(code, message);
}
