namespace SolidarityGrid.Node.Payments;

public sealed record SubmitPaymentRequest(decimal Amount, string? Currency);
