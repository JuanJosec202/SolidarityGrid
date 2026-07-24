using Microsoft.Extensions.Logging;

namespace SolidarityGrid.Infrastructure.Payments;

internal static partial class PaymentCoordinationLog
{
    [LoggerMessage(4500, LogLevel.Information, "Claim started for payment {PaymentId}. Term={Term}.", EventName = "PaymentClaimStarted")]
    public static partial void ClaimStarted(ILogger logger, Guid paymentId, long term);

    [LoggerMessage(4501, LogLevel.Information, "Peer {PeerNodeId} granted claim for payment {PaymentId}. Term={Term}.", EventName = "PaymentClaimGranted")]
    public static partial void ClaimGranted(ILogger logger, Guid paymentId, string peerNodeId, long term);

    [LoggerMessage(4502, LogLevel.Information, "Peer {PeerNodeId} rejected claim for payment {PaymentId}. Term={Term}; ErrorCode={ErrorCode}.", EventName = "PaymentClaimRejected")]
    public static partial void ClaimRejected(ILogger logger, Guid paymentId, string peerNodeId, long term, string errorCode);

    [LoggerMessage(4503, LogLevel.Information, "Ownership acquired for payment {PaymentId}. Owner={OwnerNodeId}; Term={Term}.", EventName = "PaymentOwnershipAcquired")]
    public static partial void OwnershipAcquired(ILogger logger, Guid paymentId, string ownerNodeId, long term);

    [LoggerMessage(4504, LogLevel.Information, "Processing payment {PaymentId}. Owner={OwnerNodeId}; Term={Term}; Attempt={Attempt}; SuccessfulPeers={SuccessfulPeers}.", EventName = "PaymentProcessingStarted")]
    public static partial void ProcessingStarted(ILogger logger, Guid paymentId, string ownerNodeId, long term, int attempt, int successfulPeers);

    [LoggerMessage(4505, LogLevel.Information, "Lease renewed for payment {PaymentId}. Owner={OwnerNodeId}; Term={Term}; SuccessfulPeers={SuccessfulPeers}.", EventName = "PaymentLeaseRenewed")]
    public static partial void LeaseRenewed(ILogger logger, Guid paymentId, string ownerNodeId, long term, int successfulPeers);

    [LoggerMessage(4506, LogLevel.Warning, "Processing aborted for payment {PaymentId}. Owner={OwnerNodeId}; Term={Term}; ErrorCode={ErrorCode}.", EventName = "PaymentProcessingAborted")]
    public static partial void ProcessingAborted(ILogger logger, Guid paymentId, string ownerNodeId, long term, string errorCode);

    [LoggerMessage(4507, LogLevel.Information, "Completion quorum reached for payment {PaymentId}. Owner={OwnerNodeId}; Term={Term}; SuccessfulPeers={SuccessfulPeers}.", EventName = "PaymentCompletionQuorumReached")]
    public static partial void CompletionQuorumReached(ILogger logger, Guid paymentId, string ownerNodeId, long term, int successfulPeers);

    [LoggerMessage(4508, LogLevel.Information, "Payment {PaymentId} completed successfully. Owner={OwnerNodeId}; Term={Term}; Attempt={Attempt}.", EventName = "PaymentCompleted")]
    public static partial void PaymentCompleted(ILogger logger, Guid paymentId, string ownerNodeId, long term, int attempt);

    [LoggerMessage(4509, LogLevel.Error, "Processing cycle failed for payment {PaymentId}.", EventName = "PaymentProcessingCycleFailed")]
    public static partial void PaymentFailed(ILogger logger, Exception exception, Guid paymentId);

    [LoggerMessage(4510, LogLevel.Warning, "Abandoned payment {PaymentId} detected. PreviousOwner={PreviousOwner}; LeaseExpiresAtUtc={LeaseExpiresAtUtc}; PeerHealthStatus={PeerHealthStatus}.", EventName = "AbandonedPaymentDetected")]
    public static partial void AbandonedPaymentDetected(ILogger logger, Guid paymentId, string previousOwner, DateTimeOffset leaseExpiresAtUtc, string peerHealthStatus);

    [LoggerMessage(4511, LogLevel.Warning, "Taking over payment {PaymentId}. PreviousOwner={PreviousOwner}; NewOwner={NewOwner}; PreviousTerm={PreviousTerm}; NewTerm={NewTerm}; LeaseExpiresAtUtc={LeaseExpiresAtUtc}.", EventName = "PaymentTakeoverStarted")]
    public static partial void TakeoverStarted(ILogger logger, Guid paymentId, string previousOwner, string newOwner, long previousTerm, long newTerm, DateTimeOffset leaseExpiresAtUtc);

    [LoggerMessage(4512, LogLevel.Information, "Takeover acquired for payment {PaymentId}. PreviousOwner={PreviousOwner}; NewOwner={NewOwner}; PreviousTerm={PreviousTerm}; NewTerm={NewTerm}.", EventName = "PaymentTakeoverAcquired")]
    public static partial void TakeoverAcquired(ILogger logger, Guid paymentId, string previousOwner, string newOwner, long previousTerm, long newTerm);

    [LoggerMessage(4513, LogLevel.Information, "Takeover rejected for payment {PaymentId}. PreviousOwner={PreviousOwner}; Candidate={NewOwner}; PreviousTerm={PreviousTerm}; ProposedTerm={NewTerm}; ErrorCode={ErrorCode}.", EventName = "PaymentTakeoverRejected")]
    public static partial void TakeoverRejected(ILogger logger, Guid paymentId, string previousOwner, string newOwner, long previousTerm, long newTerm, string errorCode);

    [LoggerMessage(4514, LogLevel.Information, "Recovered payment {PaymentId} processing started. PreviousOwner={PreviousOwner}; NewOwner={NewOwner}; PreviousTerm={PreviousTerm}; NewTerm={NewTerm}; Attempt={Attempt}.", EventName = "RecoveredPaymentProcessingStarted")]
    public static partial void RecoveredProcessingStarted(ILogger logger, Guid paymentId, string previousOwner, string newOwner, long previousTerm, long newTerm, int attempt);

    [LoggerMessage(4515, LogLevel.Information, "Recovered payment {PaymentId} completed. PreviousOwner={PreviousOwner}; NewOwner={NewOwner}; PreviousTerm={PreviousTerm}; NewTerm={NewTerm}; Attempt={Attempt}.", EventName = "RecoveredPaymentCompleted")]
    public static partial void RecoveredCompleted(ILogger logger, Guid paymentId, string previousOwner, string newOwner, long previousTerm, long newTerm, int attempt);
}
