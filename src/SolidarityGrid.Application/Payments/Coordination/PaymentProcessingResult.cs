namespace SolidarityGrid.Application.Payments.Coordination;

public enum PaymentProcessingStatus
{
    AcquiredAndCompleted,
    NotAcquired,
    QuorumUnavailable,
    LeaseRenewalFailed,
    CompletionQuorumUnavailable,
    Cancelled,
}

public sealed record PaymentProcessingResult(
    PaymentProcessingStatus Status,
    long? Term,
    int SuccessfulPeers,
    int LeaseRenewals)
{
    public static PaymentProcessingResult From(
        PaymentProcessingStatus status,
        long? term = null,
        int successfulPeers = 0,
        int leaseRenewals = 0) =>
        new(status, term, successfulPeers, leaseRenewals);
}
