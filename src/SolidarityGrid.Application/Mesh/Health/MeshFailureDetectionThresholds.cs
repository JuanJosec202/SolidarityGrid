namespace SolidarityGrid.Application.Mesh.Health;

public sealed record MeshFailureDetectionThresholds(
    TimeSpan SuspectAfter,
    TimeSpan UnreachableAfter);
