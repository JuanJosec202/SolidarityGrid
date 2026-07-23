using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolidarityGrid.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialNodePaymentStorage : Migration
{
    private static readonly string[] RecoveryIndexColumns =
        ["status", "lease_expires_at_utc"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    idempotency_key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "BINARY"),
                    amount = table.Column<string>(type: "TEXT", nullable: false),
                    currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    owner_node_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    term = table.Column<long>(type: "INTEGER", nullable: false),
                    lease_expires_at_utc = table.Column<long>(type: "INTEGER", nullable: true),
                    attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    version = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    completed_at_utc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.CheckConstraint("CK_payments_amount", "length(amount) > 0");
                    table.CheckConstraint("CK_payments_attempt", "attempt >= 0");
                    table.CheckConstraint("CK_payments_currency", "length(currency) = 3");
                    table.CheckConstraint("CK_payments_term", "term >= 0");
                    table.CheckConstraint("CK_payments_version", "version >= 1");
                });

        migrationBuilder.CreateIndex(
                name: "IX_payments_lease_expires_at_utc",
                table: "payments",
                column: "lease_expires_at_utc");

        migrationBuilder.CreateIndex(
                name: "IX_payments_owner_node_id",
                table: "payments",
                column: "owner_node_id");

        migrationBuilder.CreateIndex(
                name: "IX_payments_status",
                table: "payments",
                column: "status");

        migrationBuilder.CreateIndex(
                name: "IX_payments_status_lease_expires_at_utc",
                table: "payments",
                columns: RecoveryIndexColumns);

        migrationBuilder.CreateIndex(
                name: "UX_payments_idempotency_key",
                table: "payments",
                column: "idempotency_key",
                unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "payments");
    }
}
