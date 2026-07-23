namespace SolidarityGrid.Application.Payments.Replication;

public static class PaymentReplicaErrorCodes
{
    public const string PeerUnavailable = "PAYMENT_REPLICA_PEER_UNAVAILABLE";
    public const string DeadlineExceeded = "PAYMENT_REPLICA_DEADLINE_EXCEEDED";
    public const string Conflict = "PAYMENT_REPLICA_CONFLICT";
    public const string Invalid = "PAYMENT_REPLICA_INVALID";
    public const string ProtocolMismatch = "PAYMENT_REPLICA_PROTOCOL_MISMATCH";
    public const string Failed = "PAYMENT_REPLICA_FAILED";
    public const string QuorumUnavailable =
        "PAYMENT_REPLICATION_QUORUM_UNAVAILABLE";
}
