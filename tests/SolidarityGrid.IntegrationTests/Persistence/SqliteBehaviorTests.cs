using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Persistence;

public sealed class SqliteBehaviorTests
{
    [Fact]
    public async Task EveryOpenedConnectionUsesRequiredPragmas()
    {
        await using var database = new SqliteTestDatabase(busyTimeoutMilliseconds: 3210);
        await database.InitializeAsync();
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();

        Assert.Equal(1L, await ScalarInt64Async(context, "PRAGMA foreign_keys;"));
        Assert.True(
            await ScalarInt64Async(context, "PRAGMA busy_timeout;") >=
            database.BusyTimeoutMilliseconds);
        Assert.Equal("wal", await ScalarStringAsync(context, "PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task RequiredIndexesExistAndNoSpeculativeIndexWasAdded()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'payments' AND name NOT LIKE 'sqlite_autoindex_%' ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        Assert.Equal(
            [
                "IX_payments_lease_expires_at_utc",
                "IX_payments_owner_node_id",
                "IX_payments_status",
                "IX_payments_status_lease_expires_at_utc",
                "UX_payments_idempotency_key",
            ],
            names);
    }

    [Fact]
    public async Task VersionCheckConstraintRejectsInvalidRow()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            INSERT INTO payments (
                id, idempotency_key, amount, currency, status, owner_node_id,
                term, lease_expires_at_utc, attempt, version, created_at_utc,
                updated_at_utc, completed_at_utc)
            VALUES (
                '00000000-0000-0000-0000-000000000001', 'INVALID-VERSION',
                '1', 'USD', 'Received', NULL, 0, NULL, 0, 0, 1, 1, NULL);
            """;

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => command.ExecuteNonQueryAsync());
        Assert.Equal(19, exception.SqliteErrorCode);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("25.50")]
    [InlineData("150000")]
    [InlineData("999999999999.9999")]
    public async Task DecimalRoundTripIsExactAndInvariant(string invariantAmount)
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var amount = decimal.Parse(
            invariantAmount,
            System.Globalization.CultureInfo.InvariantCulture);
        var payment = PaymentPersistenceTestData.Create(
            $"AMOUNT-{invariantAmount}",
            amount);
        await using (var context = database.CreateContext())
        {
            context.Add(payment);
            await context.SaveChangesAsync();
        }

        await using var readContext = database.CreateContext();
        var actual = await readContext.Payments.SingleAsync();
        Assert.Equal(amount, actual.Amount.Amount);

        await readContext.Database.OpenConnectionAsync();
        await using var command = readContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT amount FROM payments;";
        Assert.Equal(
            amount.ToString("G29", System.Globalization.CultureInfo.InvariantCulture),
            Assert.IsType<string>(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task DateTicksPreservePreEpochAndSubMillisecondPrecision()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var createdAt = new DateTimeOffset(
            new DateTime(1965, 4, 3, 2, 1, 0, DateTimeKind.Utc).AddTicks(1234));
        var payment = PaymentPersistenceTestData.Create("PRE-EPOCH", createdAt: createdAt);
        await using (var context = database.CreateContext())
        {
            context.Add(payment);
            await context.SaveChangesAsync();
        }

        await using var readContext = database.CreateContext();
        var actual = await readContext.Payments.SingleAsync();
        Assert.Equal(createdAt, actual.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, actual.CreatedAtUtc.Offset);
        Assert.Null(actual.CompletedAtUtc);
    }

    [Fact]
    public async Task NumericLeaseSupportsOrderingAndExpirationFilter()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var earlier = PaymentPersistenceTestData.CreateProcessing("LEASE-EARLY");
        var later = PaymentPersistenceTestData.Create("LEASE-LATE");
        later.MarkReplicated(PaymentPersistenceTestData.CreatedAt.AddSeconds(1));
        later.Claim(
            new NodeId("node-b"),
            1,
            PaymentPersistenceTestData.CreatedAt.AddMinutes(20),
            PaymentPersistenceTestData.CreatedAt.AddSeconds(2));
        await using var context = database.CreateContext();
        context.AddRange(later, earlier);
        await context.SaveChangesAsync();

        var cutoff = PaymentPersistenceTestData.CreatedAt.AddMinutes(15);
        var expired = await context.Payments
            .Where(payment => payment.LeaseExpiresAtUtc <= cutoff)
            .OrderBy(payment => payment.LeaseExpiresAtUtc)
            .Select(payment => payment.IdempotencyKey.Value)
            .ToArrayAsync();

        Assert.Equal(["LEASE-EARLY"], expired);
    }

    private static async Task<long> ScalarInt64Async(
        DbContext context,
        string commandText)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScalarStringAsync(
        DbContext context,
        string commandText)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        return Convert.ToString(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
