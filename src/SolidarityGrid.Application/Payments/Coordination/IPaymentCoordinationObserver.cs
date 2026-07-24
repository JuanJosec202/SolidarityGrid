namespace SolidarityGrid.Application.Payments.Coordination;

public interface IPaymentCoordinationObserver
{
    void ClaimStarted(Guid paymentId, string correlationId, long term);

    void ClaimPeerResult(
        Guid paymentId,
        string correlationId,
        string peerNodeId,
        bool granted,
        long term,
        string? errorCode);

    void OwnershipAcquired(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term);

    void TakeoverStarted(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        long previousTerm,
        string newOwnerNodeId,
        long newTerm,
        DateTimeOffset previousLeaseExpiresAtUtc);

    void TakeoverAcquired(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        long previousTerm,
        string newOwnerNodeId,
        long newTerm);

    void TakeoverRejected(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        long previousTerm,
        string newOwnerNodeId,
        long proposedTerm,
        string errorCode);

    void ProcessingStarted(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int attempt,
        int successfulPeers);

    void LeaseRenewed(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int successfulPeers);

    void ProcessingAborted(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        string errorCode);

    void CompletionQuorumReached(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int successfulPeers);

    void PaymentCompleted(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int attempt);

    void RecoveredPaymentProcessingStarted(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        string newOwnerNodeId,
        long previousTerm,
        long newTerm,
        int attempt);

    void RecoveredPaymentCompleted(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        string newOwnerNodeId,
        long previousTerm,
        long newTerm,
        int attempt);
}
