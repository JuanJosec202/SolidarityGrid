namespace SolidarityGrid.Application.Payments.Coordination;

public static class PaymentCoordinationErrorCodes
{
    public const string ClaimGranted = "PAYMENT_CLAIM_GRANTED";
    public const string ClaimLeaseActive = "PAYMENT_CLAIM_LEASE_ACTIVE";
    public const string ClaimTermStale = "PAYMENT_CLAIM_TERM_STALE";
    public const string ClaimInvalidState = "PAYMENT_CLAIM_INVALID_STATE";
    public const string OwnerMismatch = "PAYMENT_COORDINATION_OWNER_MISMATCH";
    public const string TermMismatch = "PAYMENT_COORDINATION_TERM_MISMATCH";
    public const string LeaseExpired = "PAYMENT_COORDINATION_LEASE_EXPIRED";
    public const string PeerUnavailable =
        "PAYMENT_COORDINATION_PEER_UNAVAILABLE";
    public const string DeadlineExceeded =
        "PAYMENT_COORDINATION_DEADLINE_EXCEEDED";
    public const string QuorumUnavailable =
        "PAYMENT_COORDINATION_QUORUM_UNAVAILABLE";
    public const string Failed = "PAYMENT_COORDINATION_FAILED";
}
