namespace SolidarityGrid.Application.Payments.Coordination;

public interface IPaymentCoordinationTransport
{
    Task<IReadOnlyCollection<PaymentClaimPeerResult>> TryClaimAsync(
        PaymentClaimRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<PaymentCoordinationPeerResult>> StartProcessingAsync(
        PaymentCoordinationCommand command,
        string correlationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<PaymentCoordinationPeerResult>> RenewLeaseAsync(
        PaymentCoordinationCommand command,
        string correlationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<PaymentCoordinationPeerResult>> CompleteAsync(
        PaymentCoordinationCommand command,
        string correlationId,
        CancellationToken cancellationToken);
}
