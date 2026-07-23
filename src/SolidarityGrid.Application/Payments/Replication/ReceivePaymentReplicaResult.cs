namespace SolidarityGrid.Application.Payments.Replication;

public sealed record ReceivePaymentReplicaResult(
    bool Stored,
    bool AlreadyExisted,
    string Status,
    long Version,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ReceivePaymentReplicaResult Success(
        bool alreadyExisted,
        string status,
        long version) =>
        new(true, alreadyExisted, status, version, null, null);

    public static ReceivePaymentReplicaResult Conflict(string message) =>
        new(
            false,
            false,
            string.Empty,
            0,
            PaymentReplicaErrorCodes.Conflict,
            message);
}
