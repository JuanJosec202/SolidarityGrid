using System.ComponentModel.DataAnnotations;

namespace SolidarityGrid.Infrastructure.Mesh.Configuration;

public sealed class MeshTransportOptions
{
    public const string SectionName = "Mesh";
    public const int MaximumProbeTimeoutMilliseconds = 10_000;

    [Range(1, MaximumProbeTimeoutMilliseconds)]
    public int ProbeTimeoutMilliseconds { get; init; } = 1_000;
}
