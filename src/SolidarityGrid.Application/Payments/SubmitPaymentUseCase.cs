using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments;

public sealed class SubmitPaymentUseCase(
    IPaymentRepository paymentRepository,
    IUnitOfWork unitOfWork,
    IIdGenerator idGenerator,
    TimeProvider timeProvider)
{
    public async Task<SubmitPaymentResult> ExecuteAsync(
        SubmitPaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        IdempotencyKey idempotencyKey;
        try
        {
            idempotencyKey = new IdempotencyKey(command.IdempotencyKey);
            _ = new Money(command.Amount, command.Currency);
        }
        catch (PaymentDomainException exception)
        {
            return SubmitPaymentResult.Invalid(exception.Code, exception.Message);
        }

        var existingPayment =
            await paymentRepository.GetByIdempotencyKeyAsync(
                idempotencyKey,
                cancellationToken);
        var existingResult = EvaluateExisting(existingPayment, command);
        if (existingResult is not null)
        {
            return existingResult;
        }

        var payment = Payment.Create(
            idGenerator.NewId(),
            idempotencyKey,
            new Money(command.Amount, command.Currency),
            timeProvider.GetUtcNow());
        await paymentRepository.AddAsync(payment, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return SubmitPaymentResult.Created(PaymentMapper.ToDto(payment));
        }
        catch (DuplicatePaymentIdempotencyKeyException exception)
        {
            var winningPayment =
                await paymentRepository.GetByIdempotencyKeyAsync(
                    idempotencyKey,
                    cancellationToken);
            if (winningPayment is null)
            {
                throw new PaymentIdempotencyRaceException(
                    idempotencyKey,
                    exception);
            }

            return EvaluateExisting(winningPayment, command) ??
                throw new PaymentIdempotencyRaceException(
                    idempotencyKey,
                    exception);
        }
    }

    private static SubmitPaymentResult? EvaluateExisting(
        Payment? existingPayment,
        SubmitPaymentCommand command) =>
        PaymentIdempotencyPolicy.Evaluate(existingPayment, command) switch
        {
            PaymentIdempotencyDecision.CreateNew => null,
            PaymentIdempotencyDecision.ReplayExisting =>
                SubmitPaymentResult.Replayed(PaymentMapper.ToDto(existingPayment!)),
            PaymentIdempotencyDecision.Conflict =>
                SubmitPaymentResult.Conflict(
                    "The idempotency key was already used with a different payment payload."),
            _ => throw new InvalidOperationException(
                "Unknown payment idempotency decision."),
        };
}
