using Microsoft.Extensions.Options;

namespace SolidarityGrid.Infrastructure.Persistence.Configuration;

public sealed class PersistenceOptionsValidator : IValidateOptions<PersistenceOptions>
{
    public ValidateOptionsResult Validate(string? name, PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            failures.Add("Persistence:DatabasePath is required.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Path.GetFileName(options.DatabasePath)))
            {
                failures.Add("Persistence:DatabasePath must include a file name.");
            }

            if (!string.Equals(
                    Path.GetExtension(options.DatabasePath),
                    ".db",
                    StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("Persistence:DatabasePath must use the .db extension.");
            }
        }

        if (options.BusyTimeoutMilliseconds <= 0)
        {
            failures.Add("Persistence:BusyTimeoutMilliseconds must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
