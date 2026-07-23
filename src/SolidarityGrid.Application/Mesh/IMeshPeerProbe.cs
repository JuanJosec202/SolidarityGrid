namespace SolidarityGrid.Application.Mesh;

public interface IMeshPeerProbe
{
    Task<IReadOnlyCollection<MeshPeerProbeResult>> ProbeAllAsync(
        string correlationId,
        CancellationToken cancellationToken);
}
