using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProviderBlockListingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_provider_blocks_status_opened_at_public_id",
                table: "provider_blocks",
                columns: new[] { "status", "opened_at", "public_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_provider_blocks_status_opened_at_public_id",
                table: "provider_blocks");
        }
    }
}
