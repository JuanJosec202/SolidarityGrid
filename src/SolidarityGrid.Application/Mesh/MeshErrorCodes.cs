namespace SolidarityGrid.Application.Mesh;

public static class MeshErrorCodes
{
    public const string PeerUnreachable = "MESH_PEER_UNREACHABLE";
    public const string DeadlineExceeded = "MESH_DEADLINE_EXCEEDED";
    public const string ProtocolMismatch = "MESH_PROTOCOL_MISMATCH";
    public const string IdentityMismatch = "MESH_IDENTITY_MISMATCH";
    public const string PermissionDenied = "MESH_PERMISSION_DENIED";
    public const string Cancelled = "MESH_CANCELLED";
    public const string RpcFailure = "MESH_RPC_FAILURE";
}
