using Microsoft.EntityFrameworkCore;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Persistence;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Persistence;

public sealed class PaymentRepositoryIntegrationTests
{
    [Fact]
    public async Task MigrationCreatesSchemaAndCanRunTwice()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();
        await database.InitializeAsync();

        await using var context = database.CreateContext();
        var tables = await ReadNamesAsync(
            context,
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;");
        Assert.Contains("payments", tables);
        Assert.Contains("__EFMigrationsHistory", tables);
    }

    [Fact]
    public async Task RepositorySavesAndLoadsTrackedPaymentByIdAndKey()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var payment = PaymentPersistenceTestData.Create();

        await using var context = database.CreateContext();
        var repository = new PaymentRepository(context);
        var unitOfWork = new SolidarityGridUnitOfWork(context);
        await repository.AddAsync(payment, CancellationToken.None);
        Assert.Equal(EntityState.Added, context.Entry(payment).State);
        Assert.Equal(1, await unitOfWork.SaveChangesAsync());
        context.ChangeTracker.Clear();

        var byId = await repository.GetByIdAsync(payment.Id, CancellationToken.None);
        var byKey = await repository.GetByIdempotencyKeyAsync(
            payment.IdempotencyKey,
            CancellationToken.None);

        Assert.Same(byId, byKey);
        Assert.Equal(Microsoft.EntityFrameworkCore.EntityState.Unchanged, context.Entry(byId!).State);
    }

    [Fact]
    public async Task ReopeningContextRehydratesEveryProcessingFieldWithoutEvents()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var expected = PaymentPersistenceTestData.CreateProcessing();
        expected.DequeueDomainEvents();

        await using (var writeContext = database.CreateContext())
        {
            writeContext.Add(expected);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = database.CreateContext();
        var actual = await readContext.Payments.SingleAsync();

        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.IdempotencyKey, actual.IdempotencyKey);
        Assert.Equal(expected.Amount, actual.Amount);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.OwnerNodeId, actual.OwnerNodeId);
        Assert.Equal(expected.Term, actual.Term);
        Assert.Equal(expected.LeaseExpiresAtUtc, actual.LeaseExpiresAtUtc);
        Assert.Equal(expected.Attempt, actual.Attempt);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
        Assert.Equal(expected.CompletedAtUtc, actual.CompletedAtUtc);
        Assert.Empty(actual.DequeueDomainEvents());
    }

    [Fact]
    public async Task CompletedPaymentRoundTripsWithNullLease()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var expected = PaymentPersistenceTestData.CreateProcessing("PAY-COMPLETE");
        expected.Complete(
            new NodeId("node-a"),
            term: 1,
            PaymentPersistenceTestData.CreatedAt.AddSeconds(4));

        await using (var context = database.CreateContext())
        {
            context.Add(expected);
            await context.SaveChangesAsync();
        }

        await using var verificationContext = database.CreateContext();
        var actual = await verificationContext.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Completed, actual.Status);
        Assert.Equal(expected.CompletedAtUtc, actual.CompletedAtUtc);
        Assert.Null(actual.LeaseExpiresAtUtc);
        Assert.True(actual.IsTerminal);
    }

    [Fact]
    public async Task IdempotencyKeyIsCaseSensitive()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        await using var context = database.CreateContext();
        context.AddRange(
            PaymentPersistenceTestData.Create("PAY-1"),
            PaymentPersistenceTestData.Create("pay-1"));
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Payments.CountAsync());
    }

    [Fact]
    public async Task DuplicateKeyIsTranslatedAndOnlyOneRowRemains()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        await using (var firstContext = database.CreateContext())
        {
            firstContext.Add(PaymentPersistenceTestData.Create("DUPLICATE"));
            await firstContext.SaveChangesAsync();
        }

        await using (var secondContext = database.CreateContext())
        {
            var duplicate = PaymentPersistenceTestData.Create("DUPLICATE");
            await new PaymentRepository(secondContext).AddAsync(
                duplicate,
                CancellationToken.None);

            var exception = await Assert.ThrowsAsync<DuplicatePaymentIdempotencyKeyException>(
                () => new SolidarityGridUnitOfWork(secondContext).SaveChangesAsync());
            Assert.Equal("DUPLICATE", exception.IdempotencyKey.Value);
        }

        await using var verificationContext = database.CreateContext();
        Assert.Equal(1, await verificationContext.Payments.CountAsync());
    }

    [Fact]
    public async Task PrimaryKeyViolationRemainsATechnicalEfException()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var paymentId = Guid.NewGuid();
        await using (var firstContext = database.CreateContext())
        {
            firstContext.Add(Payment.Create(
                paymentId,
                new IdempotencyKey("FIRST-KEY"),
                new Money(10m, "USD"),
                PaymentPersistenceTestData.CreatedAt));
            await firstContext.SaveChangesAsync();
        }

        await using var secondContext = database.CreateContext();
        secondContext.Add(Payment.Create(
            paymentId,
            new IdempotencyKey("SECOND-KEY"),
            new Money(20m, "USD"),
            PaymentPersistenceTestData.CreatedAt));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => new SolidarityGridUnitOfWork(secondContext).SaveChangesAsync());
        Assert.IsNotType<PaymentPersistenceException>(exception);
    }

    [Fact]
    public async Task RepositoryHonorsCancelledQuery()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new PaymentRepository(context).GetByIdAsync(
                Guid.NewGuid(),
                cancellation.Token));
    }

    [Fact]
    public async Task ReplicatedQueryFiltersOrdersLimitsAndTracksPayments()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var later = PaymentPersistenceTestData.Create(
            "REPLICATED-LATER",
            createdAt: PaymentPersistenceTestData.CreatedAt.AddMinutes(2));
        later.MarkReplicated(PaymentPersistenceTestData.CreatedAt.AddMinutes(3));
        var earlier = PaymentPersistenceTestData.Create(
            "REPLICATED-EARLIER",
            createdAt: PaymentPersistenceTestData.CreatedAt.AddMinutes(1));
        earlier.MarkReplicated(PaymentPersistenceTestData.CreatedAt.AddMinutes(3));
        var received = PaymentPersistenceTestData.Create("RECEIVED");
        var processing = PaymentPersistenceTestData.CreateProcessing();
        var completed = PaymentPersistenceTestData.CreateProcessing("COMPLETED");
        completed.Complete(
            new NodeId("node-a"),
            1,
            PaymentPersistenceTestData.CreatedAt.AddSeconds(4));

        await using var context = database.CreateContext();
        context.AddRange(later, earlier, received, processing, completed);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new PaymentRepository(context);

        var result = await repository.GetReplicatedPaymentsAsync(
            1,
            CancellationToken.None);

        var payment = Assert.Single(result);
        Assert.Equal(earlier.Id, payment.Id);
        Assert.Equal(EntityState.Unchanged, context.Entry(payment).State);
    }

    [Fact]
    public async Task RecoverableQueryFiltersOrdersLimitsAndTracksPayments()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var cutoff = PaymentPersistenceTestData.CreatedAt.AddSeconds(20);
        var processingEarlier = CreateCoordinated(
            "RECOVER-PROCESSING",
            PaymentStatus.Processing,
            cutoff.AddSeconds(-5));
        var claimedLater = CreateCoordinated(
            "RECOVER-CLAIMED",
            PaymentStatus.Claimed,
            cutoff.AddSeconds(-1));
        var active = CreateCoordinated(
            "RECOVER-ACTIVE",
            PaymentStatus.Processing,
            cutoff.AddSeconds(1));
        var replicated = PaymentPersistenceTestData.Create("RECOVER-REPLICATED");
        replicated.MarkReplicated(
            PaymentPersistenceTestData.CreatedAt.AddSeconds(1));
        var completed = CreateCoordinated(
            "RECOVER-COMPLETED",
            PaymentStatus.Processing,
            cutoff.AddSeconds(10));
        completed.Complete(
            new NodeId("node-a"),
            1,
            PaymentPersistenceTestData.CreatedAt.AddSeconds(4));

        await using var context = database.CreateContext();
        context.AddRange(
            claimedLater,
            processingEarlier,
            active,
            replicated,
            completed);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new PaymentRepository(context);

        var limited = await repository.GetRecoverablePaymentsAsync(
            cutoff,
            1,
            CancellationToken.None);

        var first = Assert.Single(limited);
        Assert.Equal(processingEarlier.Id, first.Id);
        Assert.Equal(EntityState.Unchanged, context.Entry(first).State);

        context.ChangeTracker.Clear();
        var all = await repository.GetRecoverablePaymentsAsync(
            cutoff,
            10,
            CancellationToken.None);

        Assert.Equal(
            [processingEarlier.Id, claimedLater.Id],
            all.Select(payment => payment.Id));
    }

    [Fact]
    public async Task RecoverableQueryComparesUtcTicksAcrossOffsets()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var expirationUtc =
            PaymentPersistenceTestData.CreatedAt.AddSeconds(10);
        var payment = CreateCoordinated(
            "RECOVER-UTC-TICKS",
            PaymentStatus.Claimed,
            expirationUtc);

        await using var context = database.CreateContext();
        context.Add(payment);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var sameInstantWithOffset = expirationUtc.ToOffset(
            TimeSpan.FromHours(-5));
        var result = await new PaymentRepository(context)
            .GetRecoverablePaymentsAsync(
                sameInstantWithOffset,
                10,
                CancellationToken.None);

        Assert.Equal(payment.Id, Assert.Single(result).Id);
    }

    private static Payment CreateCoordinated(
        string key,
        PaymentStatus status,
        DateTimeOffset leaseExpiresAtUtc)
    {
        var payment = PaymentPersistenceTestData.Create(key);
        payment.MarkReplicated(
            PaymentPersistenceTestData.CreatedAt.AddSeconds(1));
        payment.Claim(
            new NodeId("node-a"),
            1,
            leaseExpiresAtUtc,
            PaymentPersistenceTestData.CreatedAt.AddSeconds(2));
        if (status == PaymentStatus.Processing)
        {
            payment.StartProcessing(
                new NodeId("node-a"),
                1,
                PaymentPersistenceTestData.CreatedAt.AddSeconds(3));
        }

        return payment;
    }

    private static async Task<string[]> ReadNamesAsync(
        SolidarityGridDbContext context,
        string commandText)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }
}
