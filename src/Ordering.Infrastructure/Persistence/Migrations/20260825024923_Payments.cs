using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payer_address",
                table: "orders",
                type: "character varying(66)",
                maxLength: 66,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_address = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    payment_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tx_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_payer_settled",
                table: "payments",
                columns: new[] { "payer_address", "settled_at" });

            migrationBuilder.CreateIndex(
                name: "ux_payments_order_id",
                table: "payments",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payments_payload_hash",
                table: "payments",
                column: "payment_payload_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payments_tx_hash",
                table: "payments",
                column: "tx_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropColumn(
                name: "payer_address",
                table: "orders");
        }
    }
}
