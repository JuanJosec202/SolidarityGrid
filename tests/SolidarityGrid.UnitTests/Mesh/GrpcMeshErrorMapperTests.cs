using Grpc.Core;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Infrastructure.Mesh;
using Xunit;

namespace SolidarityGrid.UnitTests.Mesh;

public sealed class GrpcMeshErrorMapperTests
{
    [Theory]
    [InlineData(StatusCode.Unavailable, MeshErrorCodes.PeerUnreachable)]
    [InlineData(StatusCode.DeadlineExceeded, MeshErrorCodes.DeadlineExceeded)]
    [InlineData(StatusCode.FailedPrecondition, MeshErrorCodes.ProtocolMismatch)]
    [InlineData(StatusCode.PermissionDenied, MeshErrorCodes.PermissionDenied)]
    [InlineData(StatusCode.Cancelled, MeshErrorCodes.Cancelled)]
    [InlineData(StatusCode.Internal, MeshErrorCodes.RpcFailure)]
    public void MapsGrpcStatusToNeutralError(
        StatusCode statusCode,
        string expectedErrorCode)
    {
        Assert.Equal(expectedErrorCode, GrpcMeshErrorMapper.Map(statusCode));
    }
}
