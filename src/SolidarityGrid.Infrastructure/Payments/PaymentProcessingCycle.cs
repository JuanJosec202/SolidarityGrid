using Microsoft.Extensions.Logging;
using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Application.Payments.Recovery;

namespace SolidarityGrid.Infrastructure.Payments;

public sealed class PaymentProcessingCycle(
    IPaymentRepository paymentRepository,
    IPaymentProcessor processor,
    IIdGenerator idGenerator,
    IMeshNodeIdentity localIdentity,
    IMeshPeerHealthRegistry healthRegistry,
    TimeProvider timeProvider,
    PaymentProcessingOptions options,
    ILogger<PaymentProcessingCycle> logger)
{
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var processedPaymentIds = new HashSet<Guid>();
        var payments = await paymentRepository.GetReplicatedPaymentsAsync(
            options.BatchSize,
            cancellationToken);
        foreach (var payment in payments)
        {
            await ProcessPaymentAsync(
                payment,
                processedPaymentIds,
                correlationId: null,
                cancellationToken);
        }

        var utcNow = timeProvider.GetUtcNow();
        var recoverablePayments =
            await paymentRepository.GetRecoverablePaymentsAsync(
                utcNow,
                options.BatchSize,
                cancellationToken);
        foreach (var payment in recoverablePayments)
        {
            if (processedPaymentIds.Contains(payment.Id) ||
                payment.OwnerNodeId is not { } ownerNodeId)
            {
                continue;
            }

            var ownerHealth = healthRegistry.GetSnapshot(ownerNodeId);
            var decision = PaymentRecoveryPolicy.Evaluate(
                payment,
                localIdentity.NodeId,
                ownerHealth,
                utcNow);
            if (decision != PaymentRecoveryDecision.Eligible)
            {
                continue;
            }

            var correlationId =
                $"payment-processing-{idGenerator.NewId():N}";
            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["NodeId"] = localIdentity.NodeId.Value,
                ["PaymentId"] = payment.Id,
                ["PreviousOwner"] = ownerNodeId.Value,
                ["NewOwner"] = localIdentity.NodeId.Value,
                ["PreviousTerm"] = payment.Term,
                ["LeaseExpiresAtUtc"] = payment.LeaseExpiresAtUtc,
                ["PeerHealthStatus"] = ownerHealth!.Status,
                ["CorrelationId"] = correlationId,
                ["ErrorCode"] = null,
                ["EventName"] = "PaymentRecovery",
            });
            PaymentCoordinationLog.AbandonedPaymentDetected(
                logger,
                payment.Id,
                ownerNodeId.Value,
                payment.LeaseExpiresAtUtc!.Value,
                ownerHealth.Status.ToString());
            await ProcessPaymentAsync(
                payment,
                processedPaymentIds,
                correlationId,
                cancellationToken);
        }

        return processedPaymentIds.Count;
    }

    private async Task ProcessPaymentAsync(
        Domain.Payments.Payment payment,
        HashSet<Guid> processedPaymentIds,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (!processedPaymentIds.Add(payment.Id))
        {
            return;
        }

        correlationId ??= $"payment-processing-{idGenerator.NewId():N}";
        try
        {
            await processor.ProcessAsync(
                payment,
                correlationId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            PaymentCoordinationLog.PaymentFailed(
                logger,
                exception,
                payment.Id);
        }
    }
}
