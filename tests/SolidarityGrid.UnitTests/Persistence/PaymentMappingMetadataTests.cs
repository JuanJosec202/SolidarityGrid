using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Persistence;
using SolidarityGrid.Infrastructure.Persistence.Converters;
using Xunit;

namespace SolidarityGrid.UnitTests.Persistence;

public sealed class PaymentMappingMetadataTests
{
    private readonly IEntityType _paymentType;

    public PaymentMappingMetadataTests()
    {
        var options = new DbContextOptionsBuilder<SolidarityGridDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new SolidarityGridDbContext(options);
        _paymentType = Assert.IsAssignableFrom<IEntityType>(
            context.Model.FindEntityType(typeof(Payment)));
    }

    [Fact]
    public void PaymentUsesExpectedTableAndPrimaryKey()
    {
        Assert.Equal("payments", _paymentType.GetTableName());
        var primaryKey = Assert.IsAssignableFrom<IKey>(_paymentType.FindPrimaryKey());
        Assert.Equal(nameof(Payment.Id), Assert.Single(primaryKey.Properties).Name);
        Assert.Equal(
            ValueGenerated.Never,
            RequiredProperty(nameof(Payment.Id)).ValueGenerated);
    }

    [Fact]
    public void DurableIdempotencyIndexIsUnique()
    {
        var index = Assert.Single(
            _paymentType.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "UX_payments_idempotency_key");

        Assert.True(index.IsUnique);
        Assert.Equal(nameof(Payment.IdempotencyKey), Assert.Single(index.Properties).Name);
    }

    [Fact]
    public void ScalarStorageTypesAreStable()
    {
        Assert.Equal("TEXT", RequiredProperty(nameof(Payment.Status)).GetColumnType());
        Assert.Equal(
            "INTEGER",
            RequiredProperty(nameof(Payment.LeaseExpiresAtUtc)).GetColumnType());
        Assert.Equal(
            "INTEGER",
            RequiredProperty(nameof(Payment.CreatedAtUtc)).GetColumnType());
        Assert.Equal(
            "INTEGER",
            RequiredProperty(nameof(Payment.UpdatedAtUtc)).GetColumnType());
        Assert.Equal(
            "INTEGER",
            RequiredProperty(nameof(Payment.CompletedAtUtc)).GetColumnType());

        var amountType = Assert.IsAssignableFrom<IEntityType>(
            _paymentType.FindNavigation(nameof(Payment.Amount))?.TargetEntityType);
        Assert.Equal(
            "TEXT",
            Assert.IsAssignableFrom<IProperty>(
                amountType.FindProperty(nameof(Money.Amount))).GetColumnType());
    }

    [Fact]
    public void VersionIsTheOnlyConcurrencyToken()
    {
        var concurrencyProperty = Assert.Single(
            _paymentType.GetProperties(),
            property => property.IsConcurrencyToken);

        Assert.Equal(nameof(Payment.Version), concurrencyProperty.Name);
        Assert.Equal(ValueGenerated.Never, concurrencyProperty.ValueGenerated);
    }

    [Fact]
    public void RecoveryIndexesMatchRequiredSet()
    {
        var names = _paymentType.GetIndexes()
            .Select(index => index.GetDatabaseName() ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();

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
    public void DomainEventsAreNotMapped()
    {
        Assert.Null(_paymentType.FindProperty("DomainEvents"));
        Assert.Null(_paymentType.FindNavigation("DomainEvents"));
        Assert.DoesNotContain(
            _paymentType.GetProperties(),
            property => property.Name.Contains("domainEvent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownPaymentStatusTokenIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => PaymentStatusConverter.FromToken("Unknown"));

        Assert.Contains("Unknown", exception.Message, StringComparison.Ordinal);
    }

    private IProperty RequiredProperty(string name) =>
        Assert.IsAssignableFrom<IProperty>(_paymentType.FindProperty(name));
}
