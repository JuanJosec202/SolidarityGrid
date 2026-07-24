using Microsoft.Extensions.Logging;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Payments.Coordination;

namespace SolidarityGrid.Infrastructure.Payments;

public sealed class PaymentCoordinationObserver(
    IMeshNodeIdentity localIdentity,
    ILogger<PaymentCoordinationObserver> logger) : IPaymentCoordinationObserver
{
    public void ClaimStarted(Guid paymentId, string correlationId, long term)
    {
        using var scope = BeginScope(paymentId, correlationId, null, term, null);
        PaymentCoordinationLog.ClaimStarted(logger, paymentId, term);
    }

    public void ClaimPeerResult(
        Guid paymentId,
        string correlationId,
        string peerNodeId,
        bool granted,
        long term,
        string? errorCode)
    {
        using var scope = BeginScope(paymentId, correlationId, null, term, null);
        if (granted)
        {
            PaymentCoordinationLog.ClaimGranted(
                logger,
                paymentId,
                peerNodeId,
                term);
        }
        else
        {
            PaymentCoordinationLog.ClaimRejected(
                logger,
                paymentId,
                peerNodeId,
                term,
                errorCode ?? PaymentCoordinationErrorCodes.Failed);
        }
    }

    public void OwnershipAcquired(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term)
    {
        using var scope = BeginScope(
            paymentId,
            correlationId,
            ownerNodeId,
            term,
            null);
        PaymentCoordinationLog.OwnershipAcquired(
            logger,
            paymentId,
            ownerNodeId,
            term);
    }

    public void ProcessingStarted(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int attempt,
        int successfulPeers)
    {
        using var scope = BeginScope(
            paymentId,
            correlationId,
            ownerNodeId,
            term,
            attempt);
        PaymentCoordinationLog.ProcessingStarted(
            logger,
            paymentId,
            ownerNodeId,
            term,
            attempt,
            successfulPeers);
    }

    public void LeaseRenewed(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int successfulPeers)
    {
        using var scope = BeginScope(
            paymentId,
            correlationId,
            ownerNodeId,
            term,
            null);
        PaymentCoordinationLog.LeaseRenewed(
            logger,
            paymentId,
            ownerNodeId,
            term,
            successfulPeers);
    }

    public void ProcessingAborted(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        string errorCode)
    {
        using var scope = BeginScope(
            paymentId,
            correlationId,
            ownerNodeId,
            term,
            null);
        PaymentCoordinationLog.ProcessingAborted(
            logger,
            paymentId,
            ownerNodeId,
            term,
            errorCode);
    }

    public void CompletionQuorumReached(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int successfulPeers)
    {
        using var scope = BeginScope(
            paymentId,
            correlationId,
            ownerNodeId,
            term,
            null);
        PaymentCoordinationLog.CompletionQuorumReached(
            logger,
            paymentId,
            ownerNodeId,
            term,
            successfulPeers);
    }

    public void PaymentCompleted(
        Guid paymentId,
        string correlationId,
        string ownerNodeId,
        long term,
        int attempt)
    {
        using var scope = BeginScope(
            paymentId,
            correlationId,
            ownerNodeId,
            term,
            attempt);
        PaymentCoordinationLog.PaymentCompleted(
            logger,
            paymentId,
            ownerNodeId,
            term,
            attempt);
    }

    private IDisposable? BeginScope(
        Guid paymentId,
        string correlationId,
        string? ownerNodeId,
        long term,
        int? attempt) =>
        logger.BeginScope(new Dictionary<string, object?>
        {
            ["NodeId"] = localIdentity.NodeId.Value,
            ["PaymentId"] = paymentId,
            ["OwnerNodeId"] = ownerNodeId,
            ["Term"] = term,
            ["Attempt"] = attempt,
            ["CorrelationId"] = correlationId,
            ["EventName"] = "PaymentCoordination",
        });
}
