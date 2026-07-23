using System.ComponentModel.DataAnnotations;

namespace SolidarityGrid.Node.Configuration;

public sealed class PeerOptions
{
    [Required]
    public string NodeId { get; init; } = string.Empty;

    [Required]
    [Url]
    public string Url { get; init; } = string.Empty;
}
