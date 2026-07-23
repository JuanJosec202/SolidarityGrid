namespace SolidarityGrid.Application.Payments.Replication;

public sealed record PaymentReplicationOutcome(
    bool QuorumReached,
    bool WasAlreadyReplicated,
    DateTimeOffset? ReplicatedAtUtc,
    IReadOnlyCollection<PaymentReplicaResult> ReplicaResults)
{
    public static PaymentReplicationOutcome AlreadyReplicated() =>
        new(true, true, null, Array.Empty<PaymentReplicaResult>());

    public static PaymentReplicationOutcome Reached(
        DateTimeOffset replicatedAtUtc,
        IReadOnlyCollection<PaymentReplicaResult> results) =>
        new(true, false, replicatedAtUtc, results);

    public static PaymentReplicationOutcome Unavailable(
        DateTimeOffset replicatedAtUtc,
        IReadOnlyCollection<PaymentReplicaResult> results) =>
        new(false, false, replicatedAtUtc, results);
}
