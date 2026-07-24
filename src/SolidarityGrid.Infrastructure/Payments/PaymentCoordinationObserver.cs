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

    public void TakeoverStarted(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        long previousTerm,
        string newOwnerNodeId,
        long newTerm,
        DateTimeOffset previousLeaseExpiresAtUtc)
    {
        using var scope = BeginRecoveryScope(
            paymentId,
            correlationId,
            previousOwnerNodeId,
            newOwnerNodeId,
            previousTerm,
            newTerm,
            null,
            previousLeaseExpiresAtUtc,
            null);
        PaymentCoordinationLog.TakeoverStarted(
            logger,
            paymentId,
            previousOwnerNodeId,
            newOwnerNodeId,
            previousTerm,
            newTerm,
            previousLeaseExpiresAtUtc);
    }

    public void TakeoverAcquired(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        long previousTerm,
        string newOwnerNodeId,
        long newTerm)
    {
        using var scope = BeginRecoveryScope(
            paymentId,
            correlationId,
            previousOwnerNodeId,
            newOwnerNodeId,
            previousTerm,
            newTerm,
            null,
            null,
            null);
        PaymentCoordinationLog.TakeoverAcquired(
            logger,
            paymentId,
            previousOwnerNodeId,
            newOwnerNodeId,
            previousTerm,
            newTerm);
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
        using var scope = BeginRecoveryScope(
            paymentId,
            correlationId,
            previousOwnerNodeId,
            newOwnerNodeId,
            previousTerm,
            proposedTerm,
            null,
            null,
            errorCode);
        PaymentCoordinationLog.TakeoverRejected(
            logger,
            paymentId,
            previousOwnerNodeId,
            newOwnerNodeId,
            previousTerm,
            proposedTerm,
            errorCode);
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

    public void RecoveredPaymentProcessingStarted(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        string newOwnerNodeId,
        long previousTerm,
        long newTerm,
        int attempt)
    {
        using var scope = BeginRecoveryScope(
            paymentId,
            correlationId,
            previousOwnerNodeId,
            newOwnerNodeId,
            previousTerm,
            newTerm,
            attempt,
            null,
            null);
        PaymentCoordinationLog.RecoveredProcessingStarted(
            logger,
            paymentId,
            previousOwnerNodeId,
            newOwnerNodeId,
            previousTerm,
            newTerm,
            attempt);
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
        using var scope = BeginRecoveryScope(
            paymentId,
            correlationId,
            previousOwnerNodeId,
            newOwnerNodeId,
            previousTerm,
            newTerm,
            attempt,
            null,
            null);
        PaymentCoordinationLog.RecoveredCompleted(
            logger,
            paymentId,
            previousOwnerNodeId,
            newOwnerNodeId,
            previousTerm,
            newTerm,
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

    private IDisposable? BeginRecoveryScope(
        Guid paymentId,
        string correlationId,
        string previousOwnerNodeId,
        string newOwnerNodeId,
        long previousTerm,
        long newTerm,
        int? attempt,
        DateTimeOffset? leaseExpiresAtUtc,
        string? errorCode) =>
        logger.BeginScope(new Dictionary<string, object?>
        {
            ["NodeId"] = localIdentity.NodeId.Value,
            ["PaymentId"] = paymentId,
            ["PreviousOwner"] = previousOwnerNodeId,
            ["NewOwner"] = newOwnerNodeId,
            ["PreviousTerm"] = previousTerm,
            ["NewTerm"] = newTerm,
            ["Attempt"] = attempt,
            ["CorrelationId"] = correlationId,
            ["LeaseExpiresAtUtc"] = leaseExpiresAtUtc,
            ["ErrorCode"] = errorCode,
            ["EventName"] = "PaymentRecovery",
        });
}
