using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ordering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWriteModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    total = table.Column<long>(type: "bigint", nullable: false),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    charge_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    refund_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    refund_attempts = table.Column<int>(type: "integer", nullable: false),
                    last_refund_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    next_refund_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    manual_intervention_required = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "restaurants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_restaurants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "status_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_status_history_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    base_price = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_menu_items_restaurants_restaurant_id",
                        column: x => x.restaurant_id,
                        principalTable: "restaurants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modifier_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    min_select = table.Column<int>(type: "integer", nullable: false),
                    max_select = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modifier_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_modifier_groups_menu_items_menu_item_id",
                        column: x => x.menu_item_id,
                        principalTable: "menu_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modifiers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    price_delta = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modifiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_modifiers_modifier_groups_modifier_group_id",
                        column: x => x.modifier_group_id,
                        principalTable: "modifier_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_menu_items_restaurant_id",
                table: "menu_items",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_modifier_groups_menu_item_id",
                table: "modifier_groups",
                column: "menu_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_modifiers_modifier_group_id",
                table: "modifiers",
                column: "modifier_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id_created_at",
                table: "orders",
                columns: new[] { "customer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_expires_at",
                table: "orders",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_next_refund_attempt_at",
                table: "orders",
                columns: new[] { "status", "next_refund_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_updated_at",
                table: "orders",
                columns: new[] { "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_orders_charge_id",
                table: "orders",
                column: "charge_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_orders_customer_idempotency_key",
                table: "orders",
                columns: new[] { "customer_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_processed_at",
                table: "outbox",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_restaurants_city",
                table: "restaurants",
                column: "city");

            migrationBuilder.CreateIndex(
                name: "ix_status_history_order_id",
                table: "status_history",
                column: "order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "modifiers");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "status_history");

            migrationBuilder.DropTable(
                name: "modifier_groups");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "menu_items");

            migrationBuilder.DropTable(
                name: "restaurants");
        }
    }
}
