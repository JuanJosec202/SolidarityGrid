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
}
