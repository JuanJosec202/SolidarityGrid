using Grpc.Core;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Contracts;
using SolidarityGrid.Contracts.Mesh.V1;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Node.Diagnostics;

namespace SolidarityGrid.Node.Mesh;

public sealed class MeshControlGrpcService(
    IMeshNodeIdentity localIdentity,
    IMeshPeerDirectory peerDirectory,
    TimeProvider timeProvider,
    ILogger<MeshControlGrpcService> logger) : MeshControl.MeshControlBase
{
    public override Task<ProbeResponse> Probe(
        ProbeRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        context.CancellationToken.ThrowIfCancellationRequested();

        var correlationId = GetCorrelationId(context);
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["NodeId"] = localIdentity.NodeId.Value,
            ["LocalInstanceId"] = localIdentity.InstanceId,
            ["PeerNodeId"] = request.CallerNodeId,
            ["CorrelationId"] = correlationId,
            ["EventName"] = "MeshProbeReceived",
        });

        var callerNodeId = ParseCallerNodeId(request.CallerNodeId);
        if (callerNodeId == localIdentity.NodeId)
        {
            Reject(
                request.CallerNodeId,
                "The caller cannot be the local node.",
                StatusCode.InvalidArgument);
        }

        if (!peerDirectory.Contains(callerNodeId))
        {
            Reject(
                request.CallerNodeId,
                "The caller is not a configured peer.",
                StatusCode.PermissionDenied);
        }

        if (!Guid.TryParse(request.CallerInstanceId, out var callerInstanceId) ||
            callerInstanceId == Guid.Empty)
        {
            Reject(
                request.CallerNodeId,
                "The caller InstanceId is invalid.",
                StatusCode.InvalidArgument);
        }

        if (request.ProtocolVersion != MeshProtocol.CurrentVersion)
        {
            Reject(
                request.CallerNodeId,
                "The mesh protocol version is incompatible.",
                StatusCode.FailedPrecondition);
        }

        MeshServerLog.ProbeReceived(
            logger,
            callerNodeId.Value,
            callerInstanceId,
            localIdentity.NodeId.Value,
            localIdentity.InstanceId,
            request.ProtocolVersion);

        var response = new ProbeResponse
        {
            ResponderNodeId = localIdentity.NodeId.Value,
            ResponderInstanceId = localIdentity.InstanceId.ToString("D"),
            ProtocolVersion = MeshProtocol.CurrentVersion,
            ReceivedAtUnixMilliseconds = timeProvider
                .GetUtcNow()
                .ToUnixTimeMilliseconds(),
        };

        MeshServerLog.ProbeResponded(
            logger,
            localIdentity.NodeId.Value,
            localIdentity.InstanceId,
            callerNodeId.Value,
            MeshProtocol.CurrentVersion);
        return Task.FromResult(response);
    }

    private NodeId ParseCallerNodeId(string value)
    {
        try
        {
            return new NodeId(value);
        }
        catch (PaymentDomainException)
        {
            Reject(
                value,
                "The caller NodeId is invalid.",
                StatusCode.InvalidArgument);
            throw;
        }
    }

    private void Reject(
        string peerNodeId,
        string reason,
        StatusCode statusCode)
    {
        MeshServerLog.ProbeRejected(
            logger,
            peerNodeId,
            localIdentity.NodeId.Value,
            reason,
            statusCode.ToString());
        throw new RpcException(new Status(statusCode, reason));
    }

    private static string GetCorrelationId(ServerCallContext context)
    {
        var supplied = context.RequestHeaders
            .FirstOrDefault(entry =>
                string.Equals(
                    entry.Key,
                    "x-correlation-id",
                    StringComparison.OrdinalIgnoreCase))
            ?.Value;
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            return supplied;
        }

        var httpContext = context.GetHttpContext();
        if (httpContext.Items.TryGetValue(
                CorrelationIdMiddleware.ItemName,
                out var generated) &&
            generated is string correlationId &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId;
        }

        return httpContext.TraceIdentifier;
    }
}
