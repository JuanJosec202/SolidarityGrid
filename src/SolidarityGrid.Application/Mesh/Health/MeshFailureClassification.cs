namespace SolidarityGrid.Application.Mesh.Health;

public static class MeshFailureClassification
{
    public static bool IsImmediate(string? errorCode) =>
        errorCode is MeshErrorCodes.ProtocolMismatch or
            MeshErrorCodes.IdentityMismatch or
            MeshErrorCodes.PermissionDenied;

    public static bool IsCancelled(string? errorCode) =>
        errorCode == MeshErrorCodes.Cancelled;
}
