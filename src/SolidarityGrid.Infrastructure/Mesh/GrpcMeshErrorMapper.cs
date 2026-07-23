using Grpc.Core;
using SolidarityGrid.Application.Mesh;

namespace SolidarityGrid.Infrastructure.Mesh;

public static class GrpcMeshErrorMapper
{
    public static string Map(StatusCode statusCode) => statusCode switch
    {
        StatusCode.Unavailable => MeshErrorCodes.PeerUnreachable,
        StatusCode.DeadlineExceeded => MeshErrorCodes.DeadlineExceeded,
        StatusCode.FailedPrecondition => MeshErrorCodes.ProtocolMismatch,
        StatusCode.PermissionDenied => MeshErrorCodes.PermissionDenied,
        StatusCode.Cancelled => MeshErrorCodes.Cancelled,
        _ => MeshErrorCodes.RpcFailure,
    };

    public static string GetSafeMessage(string errorCode) => errorCode switch
    {
        MeshErrorCodes.PeerUnreachable => "The mesh peer is unreachable.",
        MeshErrorCodes.DeadlineExceeded => "The mesh probe deadline was exceeded.",
        MeshErrorCodes.ProtocolMismatch => "The mesh protocol version is incompatible.",
        MeshErrorCodes.IdentityMismatch => "The peer returned an unexpected identity.",
        MeshErrorCodes.PermissionDenied => "The peer rejected the caller identity.",
        MeshErrorCodes.Cancelled => "The mesh RPC was cancelled.",
        _ => "The mesh RPC failed.",
    };
}
