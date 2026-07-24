using SolidarityGrid.Application.Payments.Coordination;

namespace SolidarityGrid.UnitTests.Payments;

internal sealed class NoOpPaymentCoordinationObserver :
    IPaymentCoordinationObserver
{
    public void ClaimStarted(Guid paymentId, string correlationId, long term)
    {
    }

    public void ClaimPeerResult(
        Guid paymentId,
        string correlationId,
        string peerNodeId,
        bool granted,
        long term,
        string? errorCode)
    {
    }

    public void OwnershipAcquired(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term)
    {
    }

    public void TakeoverStarted(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        long previousTerm,
        string newOwnerNodeId,
        long newTerm,
        DateTimeOffset previousLeaseExpiresAtUtc)
    {
    }

    public void TakeoverAcquired(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        long previousTerm,
        string newOwnerNodeId,
        long newTerm)
    {
    }

    public void TakeoverRejected(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        long previousTerm,
        string newOwnerNodeId,
        long proposedTerm,
        string errorCode)
    {
    }

    public void ProcessingStarted(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int attempt,
        int successfulPeers)
    {
    }

    public void LeaseRenewed(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int successfulPeers)
    {
    }

    public void ProcessingAborted(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        string errorCode)
    {
    }

    public void CompletionQuorumReached(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int successfulPeers)
    {
    }

    public void PaymentCompleted(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int attempt)
    {
    }

    public void RecoveredPaymentProcessingStarted(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        string newOwnerNodeId,
        long previousTerm,
        long newTerm,
        int attempt)
    {
    }

    public void RecoveredPaymentCompleted(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        string newOwnerNodeId,
        long previousTerm,
        long newTerm,
        int attempt)
    {
    }
}
