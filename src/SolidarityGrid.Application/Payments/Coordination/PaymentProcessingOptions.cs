namespace SolidarityGrid.Application.Payments.Coordination;

public sealed class PaymentProcessingOptions
{
    public const string SectionName = "PaymentProcessing";

    public int ProcessingDurationMilliseconds { get; init; } = 8_000;

    public int LeaseDurationMilliseconds { get; init; } = 4_000;

    public int LeaseRenewalIntervalMilliseconds { get; init; } = 1_000;

    public int ScanIntervalMilliseconds { get; init; } = 500;

    public int BatchSize { get; init; } = 10;
}
