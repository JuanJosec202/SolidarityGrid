namespace SolidarityGrid.Application.Mesh.Health;

public sealed record MeshPeerHealthTransition(
    MeshPeerHealthSnapshot Previous,
    MeshPeerHealthSnapshot Current,
    bool StatusChanged,
    bool Recovered,
    bool RestartDetected,
    Guid? PreviousInstanceId,
    Guid? CurrentInstanceId);
