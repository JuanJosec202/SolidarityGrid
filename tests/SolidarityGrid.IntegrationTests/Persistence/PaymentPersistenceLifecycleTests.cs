using Microsoft.EntityFrameworkCore;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Persistence;

public sealed class PaymentPersistenceLifecycleTests
{
    [Fact]
    public async Task FullLifecyclePersistsAcrossIndependentContexts()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var payment = PaymentPersistenceTestData.Create("LIFECYCLE");
        var paymentId = payment.Id;
        var nodeId = new NodeId("node-a");

        await MutateAsync(database, _ => payment);
        await MutateAsync(database, loaded =>
        {
            loaded.MarkReplicated(PaymentPersistenceTestData.CreatedAt.AddSeconds(1));
            return loaded;
        });
        await MutateAsync(database, loaded =>
        {
            loaded.Claim(
                nodeId,
                1,
                PaymentPersistenceTestData.CreatedAt.AddMinutes(10),
                PaymentPersistenceTestData.CreatedAt.AddSeconds(2));
            loaded.StartProcessing(
                nodeId,
                1,
                PaymentPersistenceTestData.CreatedAt.AddSeconds(3));
            return loaded;
        });
        await MutateAsync(database, loaded =>
        {
            loaded.RenewLease(
                nodeId,
                1,
                PaymentPersistenceTestData.CreatedAt.AddMinutes(20),
                PaymentPersistenceTestData.CreatedAt.AddSeconds(4));
            return loaded;
        });
        await MutateAsync(database, loaded =>
        {
            loaded.Complete(
                nodeId,
                1,
                PaymentPersistenceTestData.CreatedAt.AddSeconds(5));
            return loaded;
        });

        await using var finalContext = database.CreateContext();
        var completed = await finalContext.Payments.SingleAsync(
            candidate => candidate.Id == paymentId);
        Assert.Equal(PaymentStatus.Completed, completed.Status);
        Assert.Equal(nodeId, completed.OwnerNodeId);
        Assert.Equal(1, completed.Term);
        Assert.Equal(1, completed.Attempt);
        Assert.Equal(6, completed.Version);
        Assert.Equal(
            PaymentPersistenceTestData.CreatedAt.AddSeconds(5),
            completed.CompletedAtUtc);
        Assert.Null(completed.LeaseExpiresAtUtc);
        Assert.Empty(completed.DequeueDomainEvents());
    }

    private static async Task MutateAsync(
        SqliteTestDatabase database,
        Func<Payment, Payment> transition)
    {
        await using var context = database.CreateContext();
        var existing = await context.Payments.SingleOrDefaultAsync();
        var payment = transition(existing ?? PaymentPersistenceTestData.Create("LIFECYCLE"));
        if (existing is null)
        {
            context.Add(payment);
        }

        await context.SaveChangesAsync();
    }
}
