using Grpc.Core;
using System.Globalization;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Payments.Coordination;
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
    ReceivePaymentClaimUseCase receivePaymentClaim,
    ReceivePaymentProcessingStartedUseCase receiveProcessingStarted,
    ReceivePaymentLeaseRenewalUseCase receiveLeaseRenewal,
    ReceivePaymentCompletionUseCase receiveCompletion,
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

    public override async Task<TryClaimPaymentResponse> TryClaimPayment(
        TryClaimPaymentRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        context.CancellationToken.ThrowIfCancellationRequested();
        var values = ParseCoordinationValues(
            request.CallerNodeId,
            request.CallerInstanceId,
            request.ProtocolVersion,
            request.PaymentId,
            request.OwnerNodeId,
            request.Term,
            request.OccurredAtUtcTicks);
        if (!TryCreateUtc(
                request.LeaseExpiresAtUtcTicks,
                out var leaseExpiresAtUtc) ||
            leaseExpiresAtUtc <= values.OccurredAtUtc)
        {
            throw InvalidArgument("The claim lease expiration is invalid.");
        }

        var result = await receivePaymentClaim.ExecuteAsync(
            new PaymentClaimRequest(
                values.PaymentId,
                values.OwnerNodeId,
                values.Term,
                leaseExpiresAtUtc,
                values.OccurredAtUtc),
            context.CancellationToken);

        return new TryClaimPaymentResponse
        {
            ResponderNodeId = localIdentity.NodeId.Value,
            ResponderInstanceId = localIdentity.InstanceId.ToString("D"),
            PaymentId = values.PaymentId.ToString("D"),
            Granted = result.Granted,
            AlreadyApplied = result.AlreadyApplied,
            CurrentStatus = result.CurrentStatus?.ToString() ?? string.Empty,
            CurrentOwnerNodeId =
                result.CurrentOwnerNodeId?.Value ?? string.Empty,
            CurrentTerm = result.CurrentTerm,
            LeaseExpiresAtUtcTicks =
                result.LeaseExpiresAtUtc?.UtcTicks ?? 0,
            ErrorCode = result.ErrorCode ?? string.Empty,
        };
    }

    public override async Task<PaymentCoordinationResponse> StartPaymentProcessing(
        StartPaymentProcessingRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = ParseCoordinationValues(
            request.CallerNodeId,
            request.CallerInstanceId,
            request.ProtocolVersion,
            request.PaymentId,
            request.OwnerNodeId,
            request.Term,
            request.OccurredAtUtcTicks);
        var result = await receiveProcessingStarted.ExecuteAsync(
            values.ToCommand(),
            context.CancellationToken);
        return CreateCoordinationResponse(values.PaymentId, result);
    }

    public override async Task<PaymentCoordinationResponse> RenewPaymentLease(
        RenewPaymentLeaseRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = ParseCoordinationValues(
            request.CallerNodeId,
            request.CallerInstanceId,
            request.ProtocolVersion,
            request.PaymentId,
            request.OwnerNodeId,
            request.Term,
            request.OccurredAtUtcTicks);
        if (!TryCreateUtc(
                request.NewLeaseExpiresAtUtcTicks,
                out var leaseExpiresAtUtc))
        {
            throw InvalidArgument("The new lease expiration is invalid.");
        }

        var result = await receiveLeaseRenewal.ExecuteAsync(
            values.ToCommand(leaseExpiresAtUtc),
            context.CancellationToken);
        return CreateCoordinationResponse(values.PaymentId, result);
    }

    public override async Task<PaymentCoordinationResponse> CompletePayment(
        CompletePaymentRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = ParseCoordinationValues(
            request.CallerNodeId,
            request.CallerInstanceId,
            request.ProtocolVersion,
            request.PaymentId,
            request.OwnerNodeId,
            request.Term,
            request.OccurredAtUtcTicks);
        var result = await receiveCompletion.ExecuteAsync(
            values.ToCommand(),
            context.CancellationToken);
        return CreateCoordinationResponse(values.PaymentId, result);
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

    private CoordinationValues ParseCoordinationValues(
        string callerNodeIdValue,
        string callerInstanceId,
        int protocolVersion,
        string paymentIdValue,
        string ownerNodeIdValue,
        long term,
        long occurredAtUtcTicks)
    {
        NodeId callerNodeId;
        NodeId ownerNodeId;
        try
        {
            callerNodeId = new NodeId(callerNodeIdValue);
            ownerNodeId = new NodeId(ownerNodeIdValue);
        }
        catch (PaymentDomainException)
        {
            throw InvalidArgument("The coordination node identity is invalid.");
        }

        if (callerNodeId == localIdentity.NodeId)
        {
            throw InvalidArgument("The caller cannot be the local node.");
        }

        if (!peerDirectory.Contains(callerNodeId))
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "The caller is not a configured peer."));
        }

        if (callerNodeId != ownerNodeId)
        {
            throw InvalidArgument("The caller must be the proposed owner.");
        }

        if (!Guid.TryParse(callerInstanceId, out var instanceId) ||
            instanceId == Guid.Empty)
        {
            throw InvalidArgument("The caller InstanceId is invalid.");
        }

        if (protocolVersion != MeshProtocol.CurrentVersion)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "The mesh protocol version is incompatible."));
        }

        if (!Guid.TryParse(paymentIdValue, out var paymentId) ||
            paymentId == Guid.Empty)
        {
            throw InvalidArgument("The payment ID is invalid.");
        }

        if (term <= 0)
        {
            throw InvalidArgument("The coordination term must be positive.");
        }

        if (!TryCreateUtc(occurredAtUtcTicks, out var occurredAtUtc))
        {
            throw InvalidArgument("The occurrence timestamp is invalid.");
        }

        return new CoordinationValues(
            paymentId,
            ownerNodeId,
            term,
            occurredAtUtc);
    }

    private PaymentCoordinationResponse CreateCoordinationResponse(
        Guid paymentId,
        ReceivePaymentCoordinationResult result) =>
        new()
        {
            ResponderNodeId = localIdentity.NodeId.Value,
            ResponderInstanceId = localIdentity.InstanceId.ToString("D"),
            PaymentId = paymentId.ToString("D"),
            Applied = result.Applied,
            AlreadyApplied = result.AlreadyApplied,
            CurrentStatus = result.CurrentStatus?.ToString() ?? string.Empty,
            CurrentOwnerNodeId =
                result.CurrentOwnerNodeId?.Value ?? string.Empty,
            CurrentTerm = result.CurrentTerm,
            LeaseExpiresAtUtcTicks =
                result.LeaseExpiresAtUtc?.UtcTicks ?? 0,
            ErrorCode = result.ErrorCode ?? string.Empty,
        };

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

    private static RpcException InvalidArgument(string message) =>
        new(new Status(StatusCode.InvalidArgument, message));

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

    private sealed record CoordinationValues(
        Guid PaymentId,
        NodeId OwnerNodeId,
        long Term,
        DateTimeOffset OccurredAtUtc)
    {
        public PaymentCoordinationCommand ToCommand(
            DateTimeOffset? leaseExpiresAtUtc = null) =>
            new(
                PaymentId,
                OwnerNodeId,
                Term,
                OccurredAtUtc,
                leaseExpiresAtUtc);
    }
}
