using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Contracts;
using SolidarityGrid.Contracts.Mesh.V1;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Mesh.Configuration;

namespace SolidarityGrid.Infrastructure.Mesh;

public sealed class GrpcMeshPeerProbe(
    IMeshPeerDirectory peerDirectory,
    IMeshNodeIdentity localIdentity,
    GrpcMeshChannelPool channelPool,
    IOptions<MeshTransportOptions> options,
    TimeProvider timeProvider,
    ILogger<GrpcMeshPeerProbe> logger) : IMeshPeerProbe
{
    public async Task<IReadOnlyCollection<MeshPeerProbeResult>> ProbeAllAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        cancellationToken.ThrowIfCancellationRequested();

        var peers = peerDirectory.GetPeers();
        var probes = peers.Select(peer =>
            ProbePeerAsync(peer, correlationId, cancellationToken));
        return await Task.WhenAll(probes);
    }

    private async Task<MeshPeerProbeResult> ProbePeerAsync(
        MeshPeer peer,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["NodeId"] = localIdentity.NodeId.Value,
            ["LocalInstanceId"] = localIdentity.InstanceId,
            ["PeerNodeId"] = peer.NodeId.Value,
            ["CorrelationId"] = correlationId,
            ["EventName"] = "MeshProbe",
        });

        MeshTransportLog.ProbeStarted(
            logger,
            localIdentity.NodeId.Value,
            localIdentity.InstanceId,
            peer.NodeId.Value,
            MeshProtocol.CurrentVersion);

        try
        {
            var client = new MeshControl.MeshControlClient(
                channelPool.GetChannel(peer));
            var headers = new Metadata
            {
                { "x-correlation-id", ToMetadataValue(correlationId) },
            };
            var deadline = timeProvider
                .GetUtcNow()
                .AddMilliseconds(options.Value.ProbeTimeoutMilliseconds)
                .UtcDateTime;
            var request = new ProbeRequest
            {
                CallerNodeId = localIdentity.NodeId.Value,
                CallerInstanceId = localIdentity.InstanceId.ToString("D"),
                ProtocolVersion = MeshProtocol.CurrentVersion,
                SentAtUnixMilliseconds = timeProvider
                    .GetUtcNow()
                    .ToUnixTimeMilliseconds(),
            };

            var response = await client.ProbeAsync(
                request,
                headers,
                deadline,
                cancellationToken);
            var duration = timeProvider.GetElapsedTime(startedAt);
            var validationFailure = ValidateResponse(peer, response, duration);
            if (validationFailure is not null)
            {
                if (validationFailure.ErrorCode == MeshErrorCodes.IdentityMismatch)
                {
                    MeshTransportLog.PeerIdentityMismatch(
                        logger,
                        peer.NodeId.Value,
                        response.ResponderNodeId,
                        response.ResponderInstanceId,
                        response.ProtocolVersion);
                }
                else
                {
                    MeshTransportLog.ProbeFailed(
                        logger,
                        peer.NodeId.Value,
                        duration.TotalMilliseconds,
                        validationFailure.ErrorCode!,
                        StatusCode.OK.ToString());
                }

                return validationFailure;
            }

            var remoteInstanceId = Guid.Parse(response.ResponderInstanceId);
            MeshTransportLog.ProbeSucceeded(
                logger,
                peer.NodeId.Value,
                remoteInstanceId,
                duration.TotalMilliseconds,
                response.ProtocolVersion);

            return new MeshPeerProbeResult(
                peer.NodeId,
                peer.InternalUri,
                true,
                new NodeId(response.ResponderNodeId),
                remoteInstanceId,
                response.ProtocolVersion,
                duration,
                null,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RpcException exception)
            when (exception.StatusCode == StatusCode.Cancelled &&
                  cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "The mesh probe was cancelled by the caller.",
                exception,
                cancellationToken);
        }
        catch (RpcException exception)
        {
            return CreateFailure(
                peer,
                startedAt,
                GrpcMeshErrorMapper.Map(exception.StatusCode),
                exception.StatusCode.ToString());
        }
        catch (HttpRequestException)
        {
            return CreateFailure(
                peer,
                startedAt,
                MeshErrorCodes.PeerUnreachable,
                StatusCode.Unavailable.ToString());
        }
        catch (Exception exception)
            when (exception is FormatException or PaymentDomainException)
        {
            return CreateFailure(
                peer,
                startedAt,
                MeshErrorCodes.IdentityMismatch,
                StatusCode.OK.ToString());
        }
    }

    private static MeshPeerProbeResult? ValidateResponse(
        MeshPeer peer,
        ProbeResponse response,
        TimeSpan duration)
    {
        var remoteNodeId = TryCreateNodeId(response.ResponderNodeId);
        var hasInstanceId = Guid.TryParse(
            response.ResponderInstanceId,
            out var remoteInstanceId) &&
            remoteInstanceId != Guid.Empty;

        if (response.ProtocolVersion != MeshProtocol.CurrentVersion)
        {
            return ValidationFailure(
                peer,
                duration,
                remoteNodeId,
                hasInstanceId ? remoteInstanceId : null,
                response.ProtocolVersion,
                MeshErrorCodes.ProtocolMismatch);
        }

        if (remoteNodeId != peer.NodeId || !hasInstanceId)
        {
            return ValidationFailure(
                peer,
                duration,
                remoteNodeId,
                hasInstanceId ? remoteInstanceId : null,
                response.ProtocolVersion,
                MeshErrorCodes.IdentityMismatch);
        }

        return null;
    }

    private MeshPeerProbeResult CreateFailure(
        MeshPeer peer,
        long startedAt,
        string errorCode,
        string grpcStatus)
    {
        var duration = timeProvider.GetElapsedTime(startedAt);
        MeshTransportLog.ProbeFailed(
            logger,
            peer.NodeId.Value,
            duration.TotalMilliseconds,
            errorCode,
            grpcStatus);
        return Failure(peer, duration, errorCode);
    }

    private static MeshPeerProbeResult Failure(
        MeshPeer peer,
        TimeSpan duration,
        string errorCode) =>
        new(
            peer.NodeId,
            peer.InternalUri,
            false,
            null,
            null,
            null,
            duration,
            errorCode,
            GrpcMeshErrorMapper.GetSafeMessage(errorCode));

    private static MeshPeerProbeResult ValidationFailure(
        MeshPeer peer,
        TimeSpan duration,
        NodeId? remoteNodeId,
        Guid? remoteInstanceId,
        int protocolVersion,
        string errorCode) =>
        new(
            peer.NodeId,
            peer.InternalUri,
            false,
            remoteNodeId,
            remoteInstanceId,
            protocolVersion,
            duration,
            errorCode,
            GrpcMeshErrorMapper.GetSafeMessage(errorCode));

    private static NodeId? TryCreateNodeId(string value)
    {
        try
        {
            return new NodeId(value);
        }
        catch (PaymentDomainException)
        {
            return null;
        }
    }

    private static string ToMetadataValue(string correlationId)
    {
        var trimmed = correlationId.Trim();
        return trimmed.All(character => character is >= ' ' and <= '~')
            ? trimmed
            : Uri.EscapeDataString(trimmed);
    }
}
