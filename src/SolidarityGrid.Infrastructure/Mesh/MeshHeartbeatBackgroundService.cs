using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Infrastructure.Mesh.Configuration;

namespace SolidarityGrid.Infrastructure.Mesh;

public sealed class MeshHeartbeatBackgroundService(
    MeshPeerHealthMonitor monitor,
    IMeshNodeIdentity localIdentity,
    IIdGenerator idGenerator,
    IOptions<MeshFailureDetectorOptions> options,
    TimeProvider timeProvider,
    ILogger<MeshHeartbeatBackgroundService> logger) : BackgroundService
{
    public async Task<IReadOnlyCollection<MeshPeerHealthTransition>> ExecuteCycleAsync(
        CancellationToken cancellationToken)
    {
        var correlationId = $"mesh-heartbeat-{idGenerator.NewId():N}";
        return await ExecuteCycleAsync(correlationId, cancellationToken);
    }

    private async Task<IReadOnlyCollection<MeshPeerHealthTransition>> ExecuteCycleAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["NodeId"] = localIdentity.NodeId.Value,
            ["CorrelationId"] = correlationId,
            ["EventName"] = "MeshHeartbeatCycle",
        });
        MeshHeartbeatLog.CycleStarted(
            logger,
            localIdentity.NodeId.Value,
            correlationId);

        var transitions = await monitor.ObserveOnceAsync(
            correlationId,
            cancellationToken);
        foreach (var transition in transitions)
        {
            LogTransition(transition);
        }

        MeshHeartbeatLog.CycleCompleted(
            logger,
            localIdentity.NodeId.Value,
            correlationId,
            transitions.Count);
        return transitions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var correlationId = $"mesh-heartbeat-{idGenerator.NewId():N}";
            try
            {
                await ExecuteCycleAsync(correlationId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                MeshHeartbeatLog.CycleFailed(logger, exception, correlationId);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(
                        options.Value.HeartbeatIntervalMilliseconds),
                    timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void LogTransition(MeshPeerHealthTransition transition)
    {
        var current = transition.Current;
        if (!transition.StatusChanged && current.LastErrorCode is not null)
        {
            MeshHeartbeatLog.ObservationFailed(
                logger,
                current.PeerNodeId.Value,
                current.LastErrorCode,
                current.LastDuration?.TotalMilliseconds ?? 0);
        }

        if (transition.StatusChanged)
        {
            switch (current.Status)
            {
                case MeshPeerHealthStatus.Alive:
                    MeshHeartbeatLog.BecameAlive(
                        logger,
                        current.PeerNodeId.Value,
                        current.InstanceId);
                    break;
                case MeshPeerHealthStatus.Suspected:
                    MeshHeartbeatLog.Suspected(
                        logger,
                        current.PeerNodeId.Value,
                        current.ConsecutiveFailures,
                        ElapsedSinceSuccess(current).TotalMilliseconds);
                    break;
                case MeshPeerHealthStatus.Unreachable:
                    MeshHeartbeatLog.Unreachable(
                        logger,
                        current.PeerNodeId.Value,
                        current.ConsecutiveFailures,
                        current.LastErrorCode);
                    break;
                case MeshPeerHealthStatus.Unknown:
                default:
                    break;
            }
        }

        if (transition.Recovered)
        {
            MeshHeartbeatLog.Recovered(
                logger,
                current.PeerNodeId.Value,
                transition.Previous.Status.ToString(),
                current.InstanceId);
        }

        if (transition.RestartDetected)
        {
            MeshHeartbeatLog.RestartDetected(
                logger,
                current.PeerNodeId.Value,
                transition.PreviousInstanceId,
                transition.CurrentInstanceId,
                current.RestartCount);
        }
    }

    private static TimeSpan ElapsedSinceSuccess(MeshPeerHealthSnapshot snapshot) =>
        (snapshot.LastFailedProbeAtUtc ?? snapshot.StatusChangedAtUtc) -
        (snapshot.LastSuccessfulProbeAtUtc ?? snapshot.MonitoringStartedAtUtc);
}
