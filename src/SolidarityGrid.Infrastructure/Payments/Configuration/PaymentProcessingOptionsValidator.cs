using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Payments.Coordination;

namespace SolidarityGrid.Infrastructure.Payments.Configuration;

public sealed class PaymentProcessingOptionsValidator :
    IValidateOptions<PaymentProcessingOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        PaymentProcessingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var valid =
            options.ProcessingDurationMilliseconds is >= 5_000 and <= 10_000 &&
            options.LeaseRenewalIntervalMilliseconds > 0 &&
            options.LeaseDurationMilliseconds >
            options.LeaseRenewalIntervalMilliseconds &&
            options.ScanIntervalMilliseconds > 0 &&
            options.BatchSize is > 0 and <= 100;

        return valid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "PaymentProcessing requires a 5000-10000 ms processing duration, " +
                "lease duration greater than renewal interval, positive intervals, " +
                "and batch size between 1 and 100.");
    }
}
