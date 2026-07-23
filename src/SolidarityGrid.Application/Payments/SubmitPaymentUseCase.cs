using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Payments.Replication;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments;

public sealed class SubmitPaymentUseCase(
    IPaymentRepository paymentRepository,
    IUnitOfWork unitOfWork,
    IIdGenerator idGenerator,
    TimeProvider timeProvider,
    PaymentReplicationCoordinator replicationCoordinator)
{
    public async Task<SubmitPaymentResult> ExecuteAsync(
        SubmitPaymentCommand command,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

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
        var existingResult = await EvaluateExistingAsync(
            existingPayment,
            command,
            correlationId,
            cancellationToken);
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
            return await ReplicateAsync(
                payment,
                correlationId,
                isReplay: false,
                cancellationToken);
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

            return await EvaluateExistingAsync(
                winningPayment,
                command,
                correlationId,
                cancellationToken) ??
                throw new PaymentIdempotencyRaceException(
                    idempotencyKey,
                    exception);
        }
    }

    private async Task<SubmitPaymentResult?> EvaluateExistingAsync(
        Payment? existingPayment,
        SubmitPaymentCommand command,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var decision = PaymentIdempotencyPolicy.Evaluate(existingPayment, command);
        return decision switch
        {
            PaymentIdempotencyDecision.CreateNew => null,
            PaymentIdempotencyDecision.ReplayExisting =>
                await ReplicateAsync(
                    existingPayment!,
                    correlationId,
                    isReplay: true,
                    cancellationToken),
            PaymentIdempotencyDecision.Conflict =>
                SubmitPaymentResult.Conflict(
                    "The idempotency key was already used with a different payment payload."),
            _ => throw new InvalidOperationException(
                "Unknown payment idempotency decision."),
        };
    }

    private async Task<SubmitPaymentResult> ReplicateAsync(
        Payment payment,
        string correlationId,
        bool isReplay,
        CancellationToken cancellationToken)
    {
        var outcome = await replicationCoordinator.EnsureReplicatedAsync(
            payment,
            correlationId,
            cancellationToken);
        var dto = PaymentMapper.ToDto(payment);
        if (!outcome.QuorumReached)
        {
            return SubmitPaymentResult.ReplicationUnavailable(dto);
        }

        return isReplay
            ? SubmitPaymentResult.Replayed(dto)
            : SubmitPaymentResult.Created(dto);
    }
}
