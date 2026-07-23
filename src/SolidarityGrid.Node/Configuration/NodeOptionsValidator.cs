using Microsoft.Extensions.Options;

namespace SolidarityGrid.Node.Configuration;

public sealed class NodeOptionsValidator : IValidateOptions<NodeOptions>
{
    public ValidateOptionsResult Validate(string? name, NodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        ValidateRequired(options.NodeId, $"{NodeOptions.SectionName}:NodeId", failures);
        ValidateUrl(options.PublicUrl, $"{NodeOptions.SectionName}:PublicUrl", failures);
        ValidateUrl(options.InternalUrl, $"{NodeOptions.SectionName}:InternalUrl", failures);
        ValidateRequired(options.Environment, $"{NodeOptions.SectionName}:Environment", failures);

        var peers = options.Peers ?? [];
        var peerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var peer in peers)
        {
            if (peer is null)
            {
                failures.Add($"{NodeOptions.SectionName}:Peers cannot contain null entries.");
                continue;
            }

            ValidateRequired(peer.NodeId, $"{NodeOptions.SectionName}:Peers:NodeId", failures);
            ValidateUrl(peer.Url, $"{NodeOptions.SectionName}:Peers:Url", failures);

            if (!string.IsNullOrWhiteSpace(peer.NodeId) &&
                string.Equals(peer.NodeId, options.NodeId, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"Peer '{peer.NodeId}' cannot have the local NodeId.");
            }

            if (!string.IsNullOrWhiteSpace(peer.NodeId) && !peerIds.Add(peer.NodeId))
            {
                failures.Add($"Peer NodeId '{peer.NodeId}' is duplicated.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRequired(string? value, string path, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{path} is required.");
        }
    }

    private static void ValidateUrl(string? value, string path, List<string> failures)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{path} must be an absolute HTTP or HTTPS URL.");
        }
    }
}
