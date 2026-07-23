using System.Globalization;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Payments.Replication;
using SolidarityGrid.Contracts;
using SolidarityGrid.Contracts.Mesh.V1;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Mesh.Configuration;

namespace SolidarityGrid.Infrastructure.Mesh;

public sealed class GrpcPaymentReplicaTransport(
    IMeshPeerDirectory peerDirectory,
    IMeshNodeIdentity localIdentity,
    GrpcMeshChannelPool channelPool,
    IOptions<MeshTransportOptions> options,
    TimeProvider timeProvider,
    ILogger<GrpcPaymentReplicaTransport> logger) : IPaymentReplicaTransport
{
    public async Task<IReadOnlyCollection<PaymentReplicaResult>> ReplicateAsync(
        PaymentReplica replica,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replica);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        cancellationToken.ThrowIfCancellationRequested();

        var operations = peerDirectory.GetPeers().Select(peer =>
            ReplicateToPeerAsync(
                peer,
                replica,
                correlationId,
                cancellationToken));
        return await Task.WhenAll(operations);
    }

    private async Task<PaymentReplicaResult> ReplicateToPeerAsync(
        MeshPeer peer,
        PaymentReplica replica,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["NodeId"] = localIdentity.NodeId.Value,
            ["PaymentId"] = replica.PaymentId,
            ["PeerNodeId"] = peer.NodeId.Value,
            ["CorrelationId"] = correlationId,
            ["EventName"] = "PaymentReplicaTransport",
        });

        try
        {
            var client = new MeshControl.MeshControlClient(
                channelPool.GetChannel(peer));
            var response = await client.ReplicatePaymentAsync(
                CreateRequest(replica),
                new Metadata
                {
                    { "x-correlation-id", ToMetadataValue(correlationId) },
                },
                timeProvider.GetUtcNow()
                    .AddMilliseconds(options.Value.ProbeTimeoutMilliseconds)
                    .UtcDateTime,
                cancellationToken);

            if (!IsValidResponse(peer, replica.PaymentId, response))
            {
                return Failure(
                    peer,
                    replica.PaymentId,
                    PaymentReplicaErrorCodes.Failed,
                    "The replica response identity was invalid.");
            }

            PaymentReplicationLog.Stored(
                logger,
                replica.PaymentId,
                peer.NodeId.Value,
                response.AlreadyExisted);
            if (response.AlreadyExisted)
            {
                PaymentReplicationLog.AlreadyExists(
                    logger,
                    replica.PaymentId,
                    peer.NodeId.Value);
            }

            return new PaymentReplicaResult(
                peer.NodeId,
                response.Stored,
                response.AlreadyExisted,
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
                "Payment replication was cancelled by the caller.",
                exception,
                cancellationToken);
        }
        catch (RpcException exception)
        {
            var code = MapError(exception.StatusCode);
            return Failure(
                peer,
                replica.PaymentId,
                code,
                SafeMessage(code));
        }
        catch (HttpRequestException)
        {
            return Failure(
                peer,
                replica.PaymentId,
                PaymentReplicaErrorCodes.PeerUnavailable,
                SafeMessage(PaymentReplicaErrorCodes.PeerUnavailable));
        }
    }

    private ReplicatePaymentRequest CreateRequest(PaymentReplica replica) =>
        new()
        {
            CallerNodeId = localIdentity.NodeId.Value,
            CallerInstanceId = localIdentity.InstanceId.ToString("D"),
            ProtocolVersion = MeshProtocol.CurrentVersion,
            PaymentId = replica.PaymentId.ToString("D"),
            IdempotencyKey = replica.IdempotencyKey.Value,
            Amount = replica.Amount.Amount.ToString(CultureInfo.InvariantCulture),
            Currency = replica.Amount.Currency,
            CreatedAtUtcTicks = replica.CreatedAtUtc.UtcTicks,
            ReplicatedAtUtcTicks = replica.ReplicatedAtUtc.UtcTicks,
        };

    private static bool IsValidResponse(
        MeshPeer peer,
        Guid paymentId,
        ReplicatePaymentResponse response) =>
        response.Stored &&
        string.Equals(
            response.ResponderNodeId,
            peer.NodeId.Value,
            StringComparison.Ordinal) &&
        Guid.TryParse(response.ResponderInstanceId, out var instanceId) &&
        instanceId != Guid.Empty &&
        Guid.TryParse(response.PaymentId, out var responsePaymentId) &&
        responsePaymentId == paymentId &&
        response.Version >= 2 &&
        response.Status is "Replicated" or "Claimed" or "Processing" or "Completed";

    private PaymentReplicaResult Failure(
        MeshPeer peer,
        Guid paymentId,
        string errorCode,
        string errorMessage)
    {
        PaymentReplicationLog.Rejected(
            logger,
            paymentId,
            peer.NodeId.Value,
            errorCode);
        return new PaymentReplicaResult(
            peer.NodeId,
            false,
            false,
            errorCode,
            errorMessage);
    }

    private static string MapError(StatusCode statusCode) =>
        statusCode switch
        {
            StatusCode.Unavailable => PaymentReplicaErrorCodes.PeerUnavailable,
            StatusCode.DeadlineExceeded => PaymentReplicaErrorCodes.DeadlineExceeded,
            StatusCode.AlreadyExists => PaymentReplicaErrorCodes.Conflict,
            StatusCode.InvalidArgument => PaymentReplicaErrorCodes.Invalid,
            StatusCode.FailedPrecondition =>
                PaymentReplicaErrorCodes.ProtocolMismatch,
            _ => PaymentReplicaErrorCodes.Failed,
        };

    private static string SafeMessage(string errorCode) =>
        errorCode switch
        {
            PaymentReplicaErrorCodes.PeerUnavailable =>
                "The peer is unavailable.",
            PaymentReplicaErrorCodes.DeadlineExceeded =>
                "The replica deadline was exceeded.",
            PaymentReplicaErrorCodes.Conflict =>
                "The peer rejected a conflicting replica.",
            PaymentReplicaErrorCodes.Invalid =>
                "The peer rejected an invalid replica.",
            PaymentReplicaErrorCodes.ProtocolMismatch =>
                "The peer uses an incompatible protocol.",
            _ => "Payment replication failed.",
        };

    private static string ToMetadataValue(string correlationId)
    {
        var trimmed = correlationId.Trim();
        return trimmed.All(character => character is >= ' ' and <= '~')
            ? trimmed
            : Uri.EscapeDataString(trimmed);
    }
}
