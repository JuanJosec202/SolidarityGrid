using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

public interface IPaymentProcessor
{
    Task<PaymentProcessingResult> ProcessAsync(
        Payment payment,
        string correlationId,
        CancellationToken cancellationToken);
}
