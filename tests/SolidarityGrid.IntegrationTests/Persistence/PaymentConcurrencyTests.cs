using Microsoft.EntityFrameworkCore;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Persistence;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Persistence;

public sealed class PaymentConcurrencyTests
{
    [Fact]
    public async Task SecondWriterReceivesNeutralConcurrencyException()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var original = PaymentPersistenceTestData.Create("CONCURRENCY");
        original.MarkReplicated(PaymentPersistenceTestData.CreatedAt.AddSeconds(1));
        await using (var seedContext = database.CreateContext())
        {
            seedContext.Add(original);
            await seedContext.SaveChangesAsync();
        }

        await using var contextA = database.CreateContext();
        await using var contextB = database.CreateContext();
        var paymentA = await contextA.Payments.SingleAsync();
        var paymentB = await contextB.Payments.SingleAsync();
        paymentA.Claim(
            new NodeId("node-a"),
            1,
            PaymentPersistenceTestData.CreatedAt.AddMinutes(5),
            PaymentPersistenceTestData.CreatedAt.AddSeconds(2));
        paymentB.Claim(
            new NodeId("node-b"),
            1,
            PaymentPersistenceTestData.CreatedAt.AddMinutes(6),
            PaymentPersistenceTestData.CreatedAt.AddSeconds(2));

        await new SolidarityGridUnitOfWork(contextA).SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<PaymentConcurrencyException>(
            () => new SolidarityGridUnitOfWork(contextB).SaveChangesAsync());

        Assert.Equal(original.Id, exception.PaymentId);
        Assert.Equal(2, exception.ExpectedVersion);
        Assert.Equal(3, exception.ActualVersion);
    }
}
