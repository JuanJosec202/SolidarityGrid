using System.ComponentModel.DataAnnotations;

namespace SolidarityGrid.Infrastructure.Mesh.Configuration;

public sealed class MeshFailureDetectorOptions
{
    public const string SectionName = "MeshFailureDetector";
    public const int MaximumUnreachableAfterMilliseconds = 60_000;

    [Range(1, int.MaxValue)]
    public int HeartbeatIntervalMilliseconds { get; init; } = 1_000;

    [Range(1, int.MaxValue)]
    public int SuspectAfterMilliseconds { get; init; } = 3_000;

    [Range(1, MaximumUnreachableAfterMilliseconds)]
    public int UnreachableAfterMilliseconds { get; init; } = 5_000;
}
