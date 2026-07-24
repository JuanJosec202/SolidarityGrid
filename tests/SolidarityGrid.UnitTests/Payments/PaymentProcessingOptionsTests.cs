using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Infrastructure.Payments.Configuration;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentProcessingOptionsTests
{
    private readonly PaymentProcessingOptionsValidator _validator = new();

    [Fact]
    public void DefaultsAreValidAndUseEightSecondProcessing()
    {
        var options = new PaymentProcessingOptions();

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
        Assert.Equal(8_000, options.ProcessingDurationMilliseconds);
        Assert.Equal(4_000, options.LeaseDurationMilliseconds);
        Assert.Equal(1_000, options.LeaseRenewalIntervalMilliseconds);
        Assert.Equal(500, options.ScanIntervalMilliseconds);
        Assert.Equal(10, options.BatchSize);
    }

    [Theory]
    [InlineData(4_999, 4_000, 1_000, 500, 10)]
    [InlineData(10_001, 4_000, 1_000, 500, 10)]
    [InlineData(8_000, 1_000, 1_000, 500, 10)]
    [InlineData(8_000, 4_000, 0, 500, 10)]
    [InlineData(8_000, 4_000, 1_000, 0, 10)]
    [InlineData(8_000, 4_000, 1_000, 500, 0)]
    [InlineData(8_000, 4_000, 1_000, 500, 101)]
    public void InvalidValuesFailValidation(
        int processing,
        int lease,
        int renewal,
        int scan,
        int batch)
    {
        var options = new PaymentProcessingOptions
        {
            ProcessingDurationMilliseconds = processing,
            LeaseDurationMilliseconds = lease,
            LeaseRenewalIntervalMilliseconds = renewal,
            ScanIntervalMilliseconds = scan,
            BatchSize = batch,
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
    }
}
