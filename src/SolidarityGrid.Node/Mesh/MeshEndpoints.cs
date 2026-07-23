using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Contracts;
using SolidarityGrid.Node.Diagnostics;

namespace SolidarityGrid.Node.Mesh;

public static class MeshEndpoints
{
    public static IEndpointRouteBuilder MapMeshEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/mesh/peers", ProbePeersAsync);
        endpoints.MapGet("/mesh/status", GetStatus);
        return endpoints;
    }

    private static IResult GetStatus(
        IMeshNodeIdentity localIdentity,
        IMeshPeerHealthRegistry registry,
        TimeProvider timeProvider)
    {
        var peers = registry.GetSnapshots()
            .Select(snapshot => new MeshPeerStatusResponse(
                snapshot.PeerNodeId.Value,
                snapshot.InternalUri.AbsoluteUri.TrimEnd('/'),
                snapshot.Status.ToString(),
                snapshot.InstanceId,
                snapshot.MonitoringStartedAtUtc,
                snapshot.LastProbeStartedAtUtc,
                snapshot.LastSuccessfulProbeAtUtc,
                snapshot.LastFailedProbeAtUtc,
                snapshot.StatusChangedAtUtc,
                snapshot.ConsecutiveFailures,
                snapshot.TotalSuccessfulProbes,
                snapshot.TotalFailedProbes,
                snapshot.RestartCount,
                snapshot.LastDuration?.TotalMilliseconds,
                snapshot.LastErrorCode,
                snapshot.LastErrorMessage,
                snapshot.ObservationVersion))
            .ToArray();

        return Results.Ok(new MeshStatusResponse(
            localIdentity.NodeId.Value,
            localIdentity.InstanceId,
            timeProvider.GetUtcNow(),
            Array.AsReadOnly(peers)));
    }

    private static async Task<IResult> ProbePeersAsync(
        HttpContext context,
        IMeshNodeIdentity localIdentity,
        IMeshPeerProbe peerProbe,
        CancellationToken cancellationToken)
    {
        var correlationId = context.Items[CorrelationIdMiddleware.ItemName] as string
            ?? context.TraceIdentifier;
        var results = await peerProbe.ProbeAllAsync(
            correlationId,
            cancellationToken);

        return Results.Ok(new
        {
            nodeId = localIdentity.NodeId.Value,
            instanceId = localIdentity.InstanceId,
            protocolVersion = MeshProtocol.CurrentVersion,
            peers = results.Select(result => new
            {
                nodeId = result.NodeId.Value,
                internalUrl = result.InternalUri.AbsoluteUri.TrimEnd('/'),
                isReachable = result.IsReachable,
                remoteNodeId = result.RemoteNodeId?.Value,
                remoteInstanceId = result.RemoteInstanceId,
                protocolVersion = result.ProtocolVersion,
                durationMilliseconds = result.Duration.TotalMilliseconds,
                errorCode = result.ErrorCode,
                errorMessage = result.ErrorMessage,
            }),
        });
    }
}
