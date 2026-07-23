using System.ComponentModel.DataAnnotations;

namespace SolidarityGrid.Node.Configuration;

public sealed class NodeOptions
{
    public const string SectionName = "Node";

    [Required]
    public string NodeId { get; init; } = string.Empty;

    [Required]
    [Url]
    public string PublicUrl { get; init; } = string.Empty;

    [Required]
    [Url]
    public string InternalUrl { get; init; } = string.Empty;

    [Required]
    public string Environment { get; init; } = string.Empty;

    public IReadOnlyList<PeerOptions> Peers { get; init; } = [];
}
