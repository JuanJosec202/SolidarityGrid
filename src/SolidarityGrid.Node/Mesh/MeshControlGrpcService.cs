using Grpc.Core;
using System.Globalization;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Payments.Replication;
using SolidarityGrid.Contracts;
using SolidarityGrid.Contracts.Mesh.V1;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Node.Diagnostics;

namespace SolidarityGrid.Node.Mesh;

public sealed class MeshControlGrpcService(
    IMeshNodeIdentity localIdentity,
    IMeshPeerDirectory peerDirectory,
    TimeProvider timeProvider,
    ReceivePaymentReplicaUseCase receivePaymentReplica,
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

    public override async Task<ReplicatePaymentResponse> ReplicatePayment(
        ReplicatePaymentRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        context.CancellationToken.ThrowIfCancellationRequested();
        var correlationId = GetCorrelationId(context);
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["NodeId"] = localIdentity.NodeId.Value,
            ["PaymentId"] = request.PaymentId,
            ["PeerNodeId"] = request.CallerNodeId,
            ["CorrelationId"] = correlationId,
            ["EventName"] = "PaymentReplicaReceived",
        });

        var callerNodeId = ParseReplicaCallerNodeId(request.CallerNodeId);
        if (callerNodeId == localIdentity.NodeId)
        {
            RejectReplica(
                request,
                "The caller cannot be the local node.",
                StatusCode.InvalidArgument);
        }

        if (!peerDirectory.Contains(callerNodeId))
        {
            RejectReplica(
                request,
                "The caller is not a configured peer.",
                StatusCode.PermissionDenied);
        }

        if (!Guid.TryParse(request.CallerInstanceId, out var callerInstanceId) ||
            callerInstanceId == Guid.Empty)
        {
            RejectReplica(
                request,
                "The caller InstanceId is invalid.",
                StatusCode.InvalidArgument);
        }

        if (request.ProtocolVersion != MeshProtocol.CurrentVersion)
        {
            RejectReplica(
                request,
                "The mesh protocol version is incompatible.",
                StatusCode.FailedPrecondition);
        }

        var replica = ParseReplica(request);
        var result = await receivePaymentReplica.ExecuteAsync(
            replica,
            context.CancellationToken);
        if (!result.Stored)
        {
            RejectReplica(
                request,
                result.ErrorMessage ?? "The payment replica conflicts.",
                StatusCode.AlreadyExists);
        }

        if (result.AlreadyExisted)
        {
            MeshServerLog.ReplicaAlreadyExists(
                logger,
                replica.PaymentId,
                callerNodeId.Value,
                localIdentity.NodeId.Value,
                result.Status,
                result.Version);
        }
        else
        {
            MeshServerLog.ReplicaStored(
                logger,
                replica.PaymentId,
                callerNodeId.Value,
                localIdentity.NodeId.Value,
                result.Status,
                result.Version);
        }

        return new ReplicatePaymentResponse
        {
            ResponderNodeId = localIdentity.NodeId.Value,
            ResponderInstanceId = localIdentity.InstanceId.ToString("D"),
            PaymentId = replica.PaymentId.ToString("D"),
            Stored = true,
            AlreadyExisted = result.AlreadyExisted,
            Status = result.Status,
            Version = result.Version,
        };
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

    private NodeId ParseReplicaCallerNodeId(string value)
    {
        try
        {
            return new NodeId(value);
        }
        catch (PaymentDomainException)
        {
            MeshServerLog.ReplicaRejected(
                logger,
                Guid.Empty,
                value,
                localIdentity.NodeId.Value,
                PaymentReplicaErrorCodes.Invalid,
                StatusCode.InvalidArgument.ToString());
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "The caller NodeId is invalid."));
        }
    }

    private PaymentReplica ParseReplica(ReplicatePaymentRequest request)
    {
        try
        {
            if (!Guid.TryParse(request.PaymentId, out var paymentId) ||
                paymentId == Guid.Empty)
            {
                RejectReplica(
                    request,
                    "The payment ID is invalid.",
                    StatusCode.InvalidArgument);
            }

            if (!decimal.TryParse(
                    request.Amount,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var amount))
            {
                RejectReplica(
                    request,
                    "The payment amount is invalid.",
                    StatusCode.InvalidArgument);
            }

            var createdAtUtc = default(DateTimeOffset);
            var replicatedAtUtc = default(DateTimeOffset);
            if (!TryCreateUtc(request.CreatedAtUtcTicks, out createdAtUtc) ||
                !TryCreateUtc(
                    request.ReplicatedAtUtcTicks,
                    out replicatedAtUtc) ||
                replicatedAtUtc < createdAtUtc)
            {
                RejectReplica(
                    request,
                    "The payment timestamps are invalid.",
                    StatusCode.InvalidArgument);
            }

            return new PaymentReplica(
                paymentId,
                new IdempotencyKey(request.IdempotencyKey),
                new Money(amount, request.Currency),
                createdAtUtc,
                replicatedAtUtc);
        }
        catch (PaymentDomainException)
        {
            RejectReplica(
                request,
                "The payment replica payload is invalid.",
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

    private void RejectReplica(
        ReplicatePaymentRequest request,
        string reason,
        StatusCode statusCode)
    {
        _ = Guid.TryParse(request.PaymentId, out var paymentId);
        var errorCode = statusCode switch
        {
            StatusCode.AlreadyExists => PaymentReplicaErrorCodes.Conflict,
            StatusCode.FailedPrecondition =>
                PaymentReplicaErrorCodes.ProtocolMismatch,
            _ => PaymentReplicaErrorCodes.Invalid,
        };
        MeshServerLog.ReplicaRejected(
            logger,
            paymentId,
            request.CallerNodeId,
            localIdentity.NodeId.Value,
            errorCode,
            statusCode.ToString());
        throw new RpcException(new Status(statusCode, reason));
    }

    private static bool TryCreateUtc(long ticks, out DateTimeOffset value)
    {
        if (ticks < DateTimeOffset.MinValue.UtcTicks ||
            ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            value = default;
            return false;
        }

        value = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
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
