namespace SolidarityGrid.Node.Diagnostics;

public static partial class NodeLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Node {NodeId} started with {PeerCount} configured peers.")]
    public static partial void Started(ILogger logger, string nodeId, int peerCount);
}
