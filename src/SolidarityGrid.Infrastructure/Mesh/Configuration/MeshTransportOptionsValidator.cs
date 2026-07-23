using Microsoft.Extensions.Options;

namespace SolidarityGrid.Infrastructure.Mesh.Configuration;

public sealed class MeshTransportOptionsValidator :
    IValidateOptions<MeshTransportOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        MeshTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.ProbeTimeoutMilliseconds is > 0 and
            <= MeshTransportOptions.MaximumProbeTimeoutMilliseconds
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{MeshTransportOptions.SectionName}:ProbeTimeoutMilliseconds " +
                $"must be between 1 and " +
                $"{MeshTransportOptions.MaximumProbeTimeoutMilliseconds}.");
    }
}
