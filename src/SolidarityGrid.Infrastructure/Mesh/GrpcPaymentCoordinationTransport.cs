using Grpc.Core;
using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Contracts;
using SolidarityGrid.Contracts.Mesh.V1;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Mesh.Configuration;

namespace SolidarityGrid.Infrastructure.Mesh;

public sealed class GrpcPaymentCoordinationTransport(
    IMeshPeerDirectory peerDirectory,
    IMeshNodeIdentity localIdentity,
    GrpcMeshChannelPool channelPool,
    IOptions<MeshTransportOptions> options,
    TimeProvider timeProvider) : IPaymentCoordinationTransport
{
    public async Task<IReadOnlyCollection<PaymentClaimPeerResult>> TryClaimAsync(
        PaymentClaimRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCorrelationId(correlationId);
        cancellationToken.ThrowIfCancellationRequested();

        var operations = peerDirectory.GetPeers().Select(peer =>
            TryClaimPeerAsync(peer, request, correlationId, cancellationToken));
        return await Task.WhenAll(operations);
    }

    public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>>
        StartProcessingAsync(
            PaymentCoordinationCommand command,
            string correlationId,
            CancellationToken cancellationToken) =>
        SendCoordinationAsync(
            command,
            correlationId,
            (client, metadata, deadline, token) =>
                client.StartPaymentProcessingAsync(
                    CreateStartRequest(command),
                    metadata,
                    deadline,
                    token).ResponseAsync,
            cancellationToken);

    public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>> RenewLeaseAsync(
        PaymentCoordinationCommand command,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (command.LeaseExpiresAtUtc is null)
        {
            throw new ArgumentException(
                "A lease expiration is required for renewal.",
                nameof(command));
        }

        return SendCoordinationAsync(
            command,
            correlationId,
            (client, metadata, deadline, token) =>
                client.RenewPaymentLeaseAsync(
                    CreateRenewRequest(command),
                    metadata,
                    deadline,
                    token).ResponseAsync,
            cancellationToken);
    }

    public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>> CompleteAsync(
        PaymentCoordinationCommand command,
        string correlationId,
        CancellationToken cancellationToken) =>
        SendCoordinationAsync(
            command,
            correlationId,
            (client, metadata, deadline, token) =>
                client.CompletePaymentAsync(
                    CreateCompleteRequest(command),
                    metadata,
                    deadline,
                    token).ResponseAsync,
            cancellationToken);

    private async Task<PaymentClaimPeerResult> TryClaimPeerAsync(
        MeshPeer peer,
        PaymentClaimRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = new MeshControl.MeshControlClient(
                channelPool.GetChannel(peer));
            var response = await client.TryClaimPaymentAsync(
                CreateClaimRequest(request),
                CreateMetadata(correlationId),
                CreateDeadline(),
                cancellationToken);
            if (!ValidIdentity(
                    peer,
                    request.PaymentId,
                    response.ResponderNodeId,
                    response.ResponderInstanceId,
                    response.PaymentId) ||
                !TryParseOwner(response.CurrentOwnerNodeId, out var ownerNodeId) ||
                !TryParseOptionalUtc(
                    response.LeaseExpiresAtUtcTicks,
                    out var leaseExpiresAtUtc))
            {
                return ClaimFailure(
                    peer,
                    PaymentCoordinationErrorCodes.Failed);
            }

            return new PaymentClaimPeerResult(
                peer.NodeId,
                response.Granted,
                response.AlreadyApplied,
                response.CurrentTerm,
                ownerNodeId,
                leaseExpiresAtUtc,
                EmptyToNull(response.ErrorCode));
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
                "Payment claim was cancelled by the caller.",
                exception,
                cancellationToken);
        }
        catch (RpcException exception)
        {
            return ClaimFailure(peer, MapRpcError(exception.StatusCode));
        }
        catch (HttpRequestException)
        {
            return ClaimFailure(
                peer,
                PaymentCoordinationErrorCodes.PeerUnavailable);
        }
    }

    private async Task<IReadOnlyCollection<PaymentCoordinationPeerResult>>
        SendCoordinationAsync(
            PaymentCoordinationCommand command,
            string correlationId,
            Func<
                MeshControl.MeshControlClient,
                Metadata,
                DateTime,
                CancellationToken,
                Task<PaymentCoordinationResponse>> send,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCorrelationId(correlationId);
        cancellationToken.ThrowIfCancellationRequested();

        var operations = peerDirectory.GetPeers().Select(peer =>
            SendCoordinationPeerAsync(
                peer,
                command.PaymentId,
                correlationId,
                send,
                cancellationToken));
        return await Task.WhenAll(operations);
    }

    private async Task<PaymentCoordinationPeerResult> SendCoordinationPeerAsync(
        MeshPeer peer,
        Guid paymentId,
        string correlationId,
        Func<
            MeshControl.MeshControlClient,
            Metadata,
            DateTime,
            CancellationToken,
            Task<PaymentCoordinationResponse>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = new MeshControl.MeshControlClient(
                channelPool.GetChannel(peer));
            var response = await send(
                client,
                CreateMetadata(correlationId),
                CreateDeadline(),
                cancellationToken);
            if (!ValidIdentity(
                    peer,
                    paymentId,
                    response.ResponderNodeId,
                    response.ResponderInstanceId,
                    response.PaymentId))
            {
                return CoordinationFailure(
                    peer,
                    PaymentCoordinationErrorCodes.Failed);
            }

            return new PaymentCoordinationPeerResult(
                peer.NodeId,
                response.Applied,
                response.AlreadyApplied,
                EmptyToNull(response.ErrorCode));
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
                "Payment coordination was cancelled by the caller.",
                exception,
                cancellationToken);
        }
        catch (RpcException exception)
        {
            return CoordinationFailure(peer, MapRpcError(exception.StatusCode));
        }
        catch (HttpRequestException)
        {
            return CoordinationFailure(
                peer,
                PaymentCoordinationErrorCodes.PeerUnavailable);
        }
    }

    private TryClaimPaymentRequest CreateClaimRequest(PaymentClaimRequest request) =>
        new()
        {
            CallerNodeId = localIdentity.NodeId.Value,
            CallerInstanceId = localIdentity.InstanceId.ToString("D"),
            ProtocolVersion = MeshProtocol.CurrentVersion,
            PaymentId = request.PaymentId.ToString("D"),
            OwnerNodeId = request.CandidateNodeId.Value,
            Term = request.ProposedTerm,
            OccurredAtUtcTicks = request.OccurredAtUtc.UtcTicks,
            LeaseExpiresAtUtcTicks = request.LeaseExpiresAtUtc.UtcTicks,
        };

    private StartPaymentProcessingRequest CreateStartRequest(
        PaymentCoordinationCommand command) =>
        new()
        {
            CallerNodeId = localIdentity.NodeId.Value,
            CallerInstanceId = localIdentity.InstanceId.ToString("D"),
            ProtocolVersion = MeshProtocol.CurrentVersion,
            PaymentId = command.PaymentId.ToString("D"),
            OwnerNodeId = command.OwnerNodeId.Value,
            Term = command.Term,
            OccurredAtUtcTicks = command.OccurredAtUtc.UtcTicks,
        };

    private RenewPaymentLeaseRequest CreateRenewRequest(
        PaymentCoordinationCommand command) =>
        new()
        {
            CallerNodeId = localIdentity.NodeId.Value,
            CallerInstanceId = localIdentity.InstanceId.ToString("D"),
            ProtocolVersion = MeshProtocol.CurrentVersion,
            PaymentId = command.PaymentId.ToString("D"),
            OwnerNodeId = command.OwnerNodeId.Value,
            Term = command.Term,
            OccurredAtUtcTicks = command.OccurredAtUtc.UtcTicks,
            NewLeaseExpiresAtUtcTicks = command.LeaseExpiresAtUtc!.Value.UtcTicks,
        };

    private CompletePaymentRequest CreateCompleteRequest(
        PaymentCoordinationCommand command) =>
        new()
        {
            CallerNodeId = localIdentity.NodeId.Value,
            CallerInstanceId = localIdentity.InstanceId.ToString("D"),
            ProtocolVersion = MeshProtocol.CurrentVersion,
            PaymentId = command.PaymentId.ToString("D"),
            OwnerNodeId = command.OwnerNodeId.Value,
            Term = command.Term,
            OccurredAtUtcTicks = command.OccurredAtUtc.UtcTicks,
        };

    private DateTime CreateDeadline() =>
        timeProvider.GetUtcNow()
            .AddMilliseconds(options.Value.ProbeTimeoutMilliseconds)
            .UtcDateTime;

    private static Metadata CreateMetadata(string correlationId) =>
        new()
        {
            { "x-correlation-id", ToMetadataValue(correlationId) },
        };

    private static bool ValidIdentity(
        MeshPeer peer,
        Guid paymentId,
        string responderNodeId,
        string responderInstanceId,
        string responsePaymentId) =>
        string.Equals(
            responderNodeId,
            peer.NodeId.Value,
            StringComparison.Ordinal) &&
        Guid.TryParse(responderInstanceId, out var instanceId) &&
        instanceId != Guid.Empty &&
        Guid.TryParse(responsePaymentId, out var parsedPaymentId) &&
        parsedPaymentId == paymentId;

    private static bool TryParseOwner(string value, out NodeId? nodeId)
    {
        nodeId = null;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        try
        {
            nodeId = new NodeId(value);
            return true;
        }
        catch (PaymentDomainException)
        {
            return false;
        }
    }

    private static bool TryParseOptionalUtc(
        long ticks,
        out DateTimeOffset? dateTimeOffset)
    {
        dateTimeOffset = null;
        if (ticks == 0)
        {
            return true;
        }

        if (ticks < DateTimeOffset.MinValue.UtcTicks ||
            ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        dateTimeOffset = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    private static PaymentClaimPeerResult ClaimFailure(
        MeshPeer peer,
        string errorCode) =>
        new(peer.NodeId, false, false, 0, null, null, errorCode);

    private static PaymentCoordinationPeerResult CoordinationFailure(
        MeshPeer peer,
        string errorCode) =>
        new(peer.NodeId, false, false, errorCode);

    private static string MapRpcError(StatusCode statusCode) =>
        statusCode switch
        {
            StatusCode.Unavailable =>
                PaymentCoordinationErrorCodes.PeerUnavailable,
            StatusCode.DeadlineExceeded =>
                PaymentCoordinationErrorCodes.DeadlineExceeded,
            _ => PaymentCoordinationErrorCodes.Failed,
        };

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static void ValidateCorrelationId(string correlationId) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

    private static string ToMetadataValue(string correlationId)
    {
        var trimmed = correlationId.Trim();
        return trimmed.All(character => character is >= ' ' and <= '~')
            ? trimmed
            : Uri.EscapeDataString(trimmed);
    }
}
