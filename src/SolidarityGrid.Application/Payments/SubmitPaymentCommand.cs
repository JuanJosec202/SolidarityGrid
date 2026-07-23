namespace SolidarityGrid.Application.Payments;

public sealed record SubmitPaymentCommand(
    string IdempotencyKey,
    decimal Amount,
    string Currency);
