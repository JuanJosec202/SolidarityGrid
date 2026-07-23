using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Persistence.Converters;

namespace SolidarityGrid.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", table =>
        {
            table.HasCheckConstraint("CK_payments_amount", "length(amount) > 0");
            table.HasCheckConstraint("CK_payments_currency", "length(currency) = 3");
            table.HasCheckConstraint("CK_payments_term", "term >= 0");
            table.HasCheckConstraint("CK_payments_attempt", "attempt >= 0");
            table.HasCheckConstraint("CK_payments_version", "version >= 1");
        });

        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Id)
            .HasColumnName("id")
            .HasColumnType("TEXT")
            .HasConversion(
                value => value.ToString("D"),
                value => Guid.Parse(value))
            .ValueGeneratedNever();

        var idempotencyKeyComparer = new ValueComparer<IdempotencyKey>(
            (left, right) => left == right,
            value => value.GetHashCode(),
            value => new IdempotencyKey(value.Value));
        builder.Property(payment => payment.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasColumnType("TEXT")
            .HasMaxLength(IdempotencyKey.MaximumLength)
            .UseCollation("BINARY")
            .HasConversion(
                value => value.Value,
                value => new IdempotencyKey(value),
                idempotencyKeyComparer)
            .IsRequired();

        builder.OwnsOne(payment => payment.Amount, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName("amount")
                .HasColumnType("TEXT")
                .HasConversion<DecimalTextConverter>()
                .IsRequired();
            money.Property(value => value.Currency)
                .HasColumnName("currency")
                .HasColumnType("TEXT")
                .HasMaxLength(3)
                .IsRequired();
        });
        builder.Navigation(payment => payment.Amount).IsRequired();

        builder.Property(payment => payment.Status)
            .HasColumnName("status")
            .HasColumnType("TEXT")
            .HasMaxLength(32)
            .HasConversion<PaymentStatusConverter>()
            .IsRequired();

        var nodeIdComparer = new ValueComparer<NodeId?>(
            (left, right) => left == right,
            value => value == null ? 0 : value.GetHashCode(),
            value => value == null ? null : new NodeId(value.Value));
        var nodeIdConverter = new ValueConverter<NodeId?, string?>(
            value => value == null ? null : value.Value,
            value => value == null ? null : new NodeId(value));
        builder.Property(payment => payment.OwnerNodeId)
            .HasColumnName("owner_node_id")
            .HasColumnType("TEXT")
            .HasMaxLength(NodeId.MaximumLength)
            .HasConversion(nodeIdConverter, nodeIdComparer);

        builder.Property(payment => payment.Term)
            .HasColumnName("term")
            .HasColumnType("INTEGER")
            .IsRequired();
        builder.Property(payment => payment.LeaseExpiresAtUtc)
            .HasColumnName("lease_expires_at_utc")
            .HasColumnType("INTEGER")
            .HasConversion<NullableUtcTicksConverter>();
        builder.Property(payment => payment.Attempt)
            .HasColumnName("attempt")
            .HasColumnType("INTEGER")
            .IsRequired();
        builder.Property(payment => payment.Version)
            .HasColumnName("version")
            .HasColumnType("INTEGER")
            .IsConcurrencyToken()
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(payment => payment.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("INTEGER")
            .HasConversion<UtcTicksConverter>()
            .IsRequired();
        builder.Property(payment => payment.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("INTEGER")
            .HasConversion<UtcTicksConverter>()
            .IsRequired();
        builder.Property(payment => payment.CompletedAtUtc)
            .HasColumnName("completed_at_utc")
            .HasColumnType("INTEGER")
            .HasConversion<NullableUtcTicksConverter>();

        builder.HasIndex(payment => payment.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_payments_idempotency_key");
        builder.HasIndex(payment => payment.Status)
            .HasDatabaseName("IX_payments_status");
        builder.HasIndex(payment => payment.OwnerNodeId)
            .HasDatabaseName("IX_payments_owner_node_id");
        builder.HasIndex(payment => payment.LeaseExpiresAtUtc)
            .HasDatabaseName("IX_payments_lease_expires_at_utc");
        builder.HasIndex(payment => new { payment.Status, payment.LeaseExpiresAtUtc })
            .HasDatabaseName("IX_payments_status_lease_expires_at_utc");
    }
}
