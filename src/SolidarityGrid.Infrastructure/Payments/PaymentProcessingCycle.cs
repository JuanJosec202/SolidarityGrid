using Microsoft.Extensions.Logging;
using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Payments.Coordination;

namespace SolidarityGrid.Infrastructure.Payments;

public sealed class PaymentProcessingCycle(
    IPaymentRepository paymentRepository,
    IPaymentProcessor processor,
    IIdGenerator idGenerator,
    PaymentProcessingOptions options,
    ILogger<PaymentProcessingCycle> logger)
{
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var payments = await paymentRepository.GetReplicatedPaymentsAsync(
            options.BatchSize,
            cancellationToken);
        foreach (var payment in payments)
        {
            var correlationId = $"payment-processing-{idGenerator.NewId():N}";
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

        return payments.Count;
    }
}
