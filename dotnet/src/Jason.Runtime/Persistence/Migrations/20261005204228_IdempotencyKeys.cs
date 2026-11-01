using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_digest",
                table: "work_items",
                type: "TEXT",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "work_items",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_digest",
                table: "campaigns",
                type: "TEXT",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "campaigns",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_items_one_per_campaign_idempotency_key",
                table: "work_items",
                columns: new[] { "campaign_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_one_per_idempotency_key",
                table: "campaigns",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_work_items_one_per_campaign_idempotency_key",
                table: "work_items");

            migrationBuilder.DropIndex(
                name: "ix_campaigns_one_per_idempotency_key",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "idempotency_digest",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "idempotency_digest",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "campaigns");
        }
    }
}
