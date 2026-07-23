using Microsoft.Extensions.Options;

namespace SolidarityGrid.Infrastructure.Mesh.Configuration;

public sealed class MeshFailureDetectorOptionsValidator :
    IValidateOptions<MeshFailureDetectorOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        MeshFailureDetectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var valid = options.HeartbeatIntervalMilliseconds > 0 &&
                    options.SuspectAfterMilliseconds >
                    options.HeartbeatIntervalMilliseconds &&
                    options.UnreachableAfterMilliseconds >
                    options.SuspectAfterMilliseconds &&
                    options.UnreachableAfterMilliseconds <=
                    MeshFailureDetectorOptions.MaximumUnreachableAfterMilliseconds;

        return valid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "MeshFailureDetector intervals must satisfy: heartbeat > 0, " +
                "suspect > heartbeat, unreachable > suspect, and " +
                "unreachable <= 60000.");
    }
}
