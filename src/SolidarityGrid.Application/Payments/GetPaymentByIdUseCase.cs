using SolidarityGrid.Application.Abstractions.Persistence;

namespace SolidarityGrid.Application.Payments;

public sealed class GetPaymentByIdUseCase(IPaymentRepository paymentRepository)
{
    public async Task<PaymentDto?> ExecuteAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (paymentId == Guid.Empty)
        {
            return null;
        }

        var payment = await paymentRepository.GetByIdAsync(
            paymentId,
            cancellationToken);
        return payment is null ? null : PaymentMapper.ToDto(payment);
    }
}
