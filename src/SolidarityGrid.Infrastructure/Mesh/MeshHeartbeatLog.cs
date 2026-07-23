using Microsoft.Extensions.Logging;

namespace SolidarityGrid.Infrastructure.Mesh;

internal static partial class MeshHeartbeatLog
{
    [LoggerMessage(4300, LogLevel.Debug, "Mesh heartbeat cycle {CorrelationId} started for {LocalNodeId}.", EventName = "MeshHeartbeatCycleStarted")]
    public static partial void CycleStarted(ILogger logger, string localNodeId, string correlationId);

    [LoggerMessage(4301, LogLevel.Debug, "Mesh heartbeat cycle {CorrelationId} completed for {LocalNodeId} with {PeerCount} observations.", EventName = "MeshHeartbeatCycleCompleted")]
    public static partial void CycleCompleted(ILogger logger, string localNodeId, string correlationId, int peerCount);

    [LoggerMessage(4302, LogLevel.Information, "Peer {PeerNodeId} became alive with instance {PeerInstanceId}.", EventName = "MeshPeerBecameAlive")]
    public static partial void BecameAlive(ILogger logger, string peerNodeId, Guid? peerInstanceId);

    [LoggerMessage(4303, LogLevel.Warning, "Peer {PeerNodeId} is suspected after {ConsecutiveFailures} consecutive failures; elapsed {ElapsedMilliseconds} ms.", EventName = "MeshPeerSuspected")]
    public static partial void Suspected(ILogger logger, string peerNodeId, int consecutiveFailures, double elapsedMilliseconds);

    [LoggerMessage(4304, LogLevel.Warning, "Peer {PeerNodeId} is unreachable after {ConsecutiveFailures} consecutive failures with {ErrorCode}.", EventName = "MeshPeerUnreachable")]
    public static partial void Unreachable(ILogger logger, string peerNodeId, int consecutiveFailures, string? errorCode);

    [LoggerMessage(4305, LogLevel.Information, "Peer {PeerNodeId} recovered from {PreviousStatus} with instance {PeerInstanceId}.", EventName = "MeshPeerRecovered")]
    public static partial void Recovered(ILogger logger, string peerNodeId, string previousStatus, Guid? peerInstanceId);

    [LoggerMessage(4306, LogLevel.Information, "Peer {PeerNodeId} restarted. PreviousInstance={PreviousInstanceId} NewInstance={NewInstanceId} RestartCount={RestartCount}.", EventName = "MeshPeerRestartDetected")]
    public static partial void RestartDetected(ILogger logger, string peerNodeId, Guid? previousInstanceId, Guid? newInstanceId, long restartCount);

    [LoggerMessage(4307, LogLevel.Debug, "Peer {PeerNodeId} observation failed with {ErrorCode} in {DurationMilliseconds} ms.", EventName = "MeshPeerObservationFailed")]
    public static partial void ObservationFailed(ILogger logger, string peerNodeId, string? errorCode, double durationMilliseconds);

    [LoggerMessage(4308, LogLevel.Error, "Unexpected failure in mesh heartbeat cycle {CorrelationId}. The next cycle will still run.", EventName = "MeshHeartbeatCycleFailed")]
    public static partial void CycleFailed(ILogger logger, Exception exception, string correlationId);
}
