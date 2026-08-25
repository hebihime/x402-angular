using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReadModelPaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE read_orders
                    ADD COLUMN payer_address text NULL,
                    ADD COLUMN payment_tx_hash text NULL,
                    ADD COLUMN refund_tx_hash text NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE read_orders
                    DROP COLUMN IF EXISTS payer_address,
                    DROP COLUMN IF EXISTS payment_tx_hash,
                    DROP COLUMN IF EXISTS refund_tx_hash;
                """);
        }
    }
}
