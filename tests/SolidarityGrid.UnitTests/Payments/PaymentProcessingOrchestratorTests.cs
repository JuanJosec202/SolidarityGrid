using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentProcessingOrchestratorTests
{
    [Fact]
    public async Task HappyPathStartsRenewsAndCompletesAfterRemoteQuorum()
    {
        var fixture = CreateFixture();
        var payment = CreateReplicated(fixture.TimeProvider);

        var result = await fixture.Orchestrator.ProcessAsync(
            payment,
            "happy-path",
            CancellationToken.None);

        Assert.Equal(
            PaymentProcessingStatus.AcquiredAndCompleted,
            result.Status);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(1, payment.Attempt);
        Assert.True(result.LeaseRenewals > 0);
        Assert.Equal("claim", fixture.Transport.Operations[0]);
        Assert.Equal("complete", fixture.Transport.Operations[^1]);
        Assert.Contains("renew", fixture.Transport.Operations);
    }

    [Fact]
    public async Task StartWithoutQuorumDoesNotProcess()
    {
        var fixture = CreateFixture(startSucceeds: false);
        var payment = CreateReplicated(fixture.TimeProvider);

        var result = await fixture.Orchestrator.ProcessAsync(
            payment,
            "start-no-quorum",
            CancellationToken.None);

        Assert.Equal(PaymentProcessingStatus.QuorumUnavailable, result.Status);
        Assert.Equal(PaymentStatus.Claimed, payment.Status);
        Assert.Equal(0, payment.Attempt);
        Assert.DoesNotContain("complete", fixture.Transport.Operations);
    }

    [Fact]
    public async Task RenewalWithoutQuorumAbortsBeforeCompletion()
    {
        var fixture = CreateFixture(renewSucceeds: false);
        var payment = CreateReplicated(fixture.TimeProvider);

        var result = await fixture.Orchestrator.ProcessAsync(
            payment,
            "renew-no-quorum",
            CancellationToken.None);

        Assert.Equal(
            PaymentProcessingStatus.LeaseRenewalFailed,
            result.Status);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.DoesNotContain("complete", fixture.Transport.Operations);
    }

    [Fact]
    public async Task CompletionWithoutQuorumLeavesPaymentProcessing()
    {
        var fixture = CreateFixture(completeSucceeds: false);
        var payment = CreateReplicated(fixture.TimeProvider);

        var result = await fixture.Orchestrator.ProcessAsync(
            payment,
            "complete-no-quorum",
            CancellationToken.None);

        Assert.Equal(
            PaymentProcessingStatus.CompletionQuorumUnavailable,
            result.Status);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Null(payment.CompletedAtUtc);
    }

    [Fact]
    public async Task CancellationReturnsCancelledWithoutMutation()
    {
        var fixture = CreateFixture();
        var payment = CreateReplicated(fixture.TimeProvider);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await fixture.Orchestrator.ProcessAsync(
            payment,
            "cancelled",
            cancellation.Token);

        Assert.Equal(PaymentProcessingStatus.Cancelled, result.Status);
        Assert.Equal(PaymentStatus.Replicated, payment.Status);
    }

    [Fact]
    public async Task LocalCompletionSaveOccursAfterRemoteCompletion()
    {
        var fixture = CreateFixture();
        var payment = CreateReplicated(fixture.TimeProvider);
        fixture.UnitOfWork.Payment = payment;
        fixture.UnitOfWork.Operations = fixture.Transport.Operations;

        await fixture.Orchestrator.ProcessAsync(
            payment,
            "remote-first",
            CancellationToken.None);

        var remoteCompletionIndex =
            fixture.Transport.Operations.IndexOf("complete");
        var localCompletionIndex =
            fixture.Transport.Operations.IndexOf("save-Completed");
        Assert.True(remoteCompletionIndex >= 0);
        Assert.True(localCompletionIndex > remoteCompletionIndex);
    }

    [Fact]
    public async Task TakeoverReusesFlowWithHigherTermAndSecondAttempt()
    {
        var fixture = CreateFixture();
        var payment = CreateExpiredProcessing(fixture.TimeProvider);
        fixture.UnitOfWork.Payment = payment;
        fixture.UnitOfWork.Operations = fixture.Transport.Operations;

        var result = await fixture.Orchestrator.ProcessAsync(
            payment,
            "takeover-flow",
            CancellationToken.None);

        Assert.Equal(
            PaymentProcessingStatus.AcquiredAndCompleted,
            result.Status);
        Assert.Equal(
            PaymentProcessingMode.TakeoverProcessing,
            result.Mode);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(new NodeId("node-a"), payment.OwnerNodeId);
        Assert.Equal(2, payment.Term);
        Assert.Equal(2, payment.Attempt);
        Assert.True(result.LeaseRenewals > 0);
        var localStart = fixture.Transport.Operations.IndexOf("save-Processing");
        var remoteRenew = fixture.Transport.Operations.IndexOf("renew");
        var localRenew = fixture.Transport.Operations.IndexOf(
            "save-Processing",
            localStart + 1);
        Assert.True(fixture.Transport.Operations.IndexOf("start") < localStart);
        Assert.True(remoteRenew < localRenew);
        Assert.True(
            fixture.Transport.Operations.IndexOf("complete") <
            fixture.Transport.Operations.IndexOf("save-Completed"));
    }

    private static Fixture CreateFixture(
        bool startSucceeds = true,
        bool renewSucceeds = true,
        bool completeSucceeds = true)
    {
        var options = new PaymentProcessingOptions
        {
            ProcessingDurationMilliseconds = 30,
            LeaseDurationMilliseconds = 100,
            LeaseRenewalIntervalMilliseconds = 5,
            ScanIntervalMilliseconds = 5,
            BatchSize = 1,
        };
        var identity = new FakeIdentity();
        var transport = new FakeTransport(
            startSucceeds,
            renewSucceeds,
            completeSucceeds);
        var unitOfWork = new RecordingUnitOfWork();
        var observer = new NoOpPaymentCoordinationObserver();
        var timeProvider = new AutoAdvanceTimeProvider(
            PaymentTestData.CreatedAt.AddMinutes(1));
        var ownership = new PaymentOwnershipCoordinator(
            transport,
            unitOfWork,
            timeProvider,
            identity,
            new EmptyDirectory(),
            options,
            observer);
        var orchestrator = new PaymentProcessingOrchestrator(
            ownership,
            transport,
            unitOfWork,
            timeProvider,
            identity,
            options,
            observer);
        return new Fixture(orchestrator, transport, unitOfWork, timeProvider);
    }

    private static Payment CreateReplicated(TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow().AddSeconds(-1);
        var payment = Payment.Create(
            PaymentTestData.PaymentId,
            new IdempotencyKey("PROCESS-ORCHESTRATOR"),
            new Money(42m, "USD"),
            now);
        payment.MarkReplicated(now.AddMilliseconds(1));
        return payment;
    }

    private static Payment CreateExpiredProcessing(TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();
        var createdAt = now.AddSeconds(-10);
        var owner = new NodeId("node-b");
        var payment = Payment.Create(
            Guid.NewGuid(),
            new IdempotencyKey("PROCESS-TAKEOVER"),
            new Money(42m, "USD"),
            createdAt);
        payment.MarkReplicated(createdAt.AddSeconds(1));
        payment.Claim(
            owner,
            1,
            now.AddSeconds(-1),
            createdAt.AddSeconds(2));
        payment.StartProcessing(owner, 1, createdAt.AddSeconds(3));
        return payment;
    }

    private sealed record Fixture(
        PaymentProcessingOrchestrator Orchestrator,
        FakeTransport Transport,
        RecordingUnitOfWork UnitOfWork,
        TimeProvider TimeProvider);

    private sealed class FakeIdentity : IMeshNodeIdentity
    {
        public NodeId NodeId { get; } = new("node-a");

        public Guid InstanceId { get; } = Guid.NewGuid();
    }

    private sealed class EmptyDirectory : IMeshPeerDirectory
    {
        public IReadOnlyCollection<MeshPeer> GetPeers() => [];

        public bool Contains(NodeId nodeId) => false;
    }

    private sealed class FakeTransport(
        bool startSucceeds,
        bool renewSucceeds,
        bool completeSucceeds) : IPaymentCoordinationTransport
    {
        public List<string> Operations { get; } = [];

        public Task<IReadOnlyCollection<PaymentClaimPeerResult>> TryClaimAsync(
            PaymentClaimRequest request,
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("claim");
            IReadOnlyCollection<PaymentClaimPeerResult> result =
            [
                new(
                    new NodeId("node-b"),
                    true,
                    false,
                    request.ProposedTerm,
                    request.CandidateNodeId,
                    request.LeaseExpiresAtUtc,
                    PaymentCoordinationErrorCodes.ClaimGranted),
            ];
            return Task.FromResult(result);
        }

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>>
            StartProcessingAsync(
                PaymentCoordinationCommand command,
                string correlationId,
                CancellationToken cancellationToken)
        {
            Operations.Add("start");
            return Task.FromResult(Result(startSucceeds));
        }

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>> RenewLeaseAsync(
            PaymentCoordinationCommand command,
            string correlationId,
            CancellationToken cancellationToken)
        {
            Operations.Add("renew");
            return Task.FromResult(Result(renewSucceeds));
        }

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>> CompleteAsync(
            PaymentCoordinationCommand command,
            string correlationId,
            CancellationToken cancellationToken)
        {
            Operations.Add("complete");
            return Task.FromResult(Result(completeSucceeds));
        }

        private static IReadOnlyCollection<PaymentCoordinationPeerResult> Result(
            bool succeeds) =>
        [
            new(
                new NodeId("node-b"),
                succeeds,
                false,
                succeeds
                    ? null
                    : PaymentCoordinationErrorCodes.PeerUnavailable),
        ];
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public Payment? Payment { get; set; }

        public List<string>? Operations { get; set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Payment is not null && Operations is not null)
            {
                Operations.Add($"save-{Payment.Status}");
            }

            return Task.FromResult(1);
        }
    }

    private sealed class AutoAdvanceTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        private readonly object _sync = new();
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _utcNow;
            }
        }

        public override long GetTimestamp()
        {
            lock (_sync)
            {
                return _timestamp;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return new AutoAdvanceTimer(this, callback, state, dueTime, period);
        }

        private void Advance(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero ||
                duration == Timeout.InfiniteTimeSpan)
            {
                return;
            }

            lock (_sync)
            {
                _utcNow = _utcNow.Add(duration);
                _timestamp += duration.Ticks;
            }
        }

        private sealed class AutoAdvanceTimer : ITimer
        {
            private readonly AutoAdvanceTimeProvider _timeProvider;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private TimeSpan _period;
            private int _disposed;

            public AutoAdvanceTimer(
                AutoAdvanceTimeProvider timeProvider,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                _timeProvider = timeProvider;
                _callback = callback;
                _state = state;
                _period = period;
                Schedule(dueTime);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return false;
                }

                _period = period;
                Schedule(dueTime);
                return true;
            }

            public void Dispose() =>
                Interlocked.Exchange(ref _disposed, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            private void Schedule(TimeSpan dueTime)
            {
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    return;
                }

                _timeProvider.Advance(dueTime);
                _ = ThreadPool.QueueUserWorkItem(
                    static timer => ((AutoAdvanceTimer)timer!).Invoke(),
                    this);
            }

            private void Invoke()
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                _callback(_state);
                if (_period != Timeout.InfiniteTimeSpan &&
                    _period > TimeSpan.Zero &&
                    Volatile.Read(ref _disposed) == 0)
                {
                    Schedule(_period);
                }
            }
        }
    }
}
