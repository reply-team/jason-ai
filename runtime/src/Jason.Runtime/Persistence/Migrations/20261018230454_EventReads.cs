using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failures",
                table: "event_sources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "last_class",
                table: "event_sources",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_code",
                table: "event_sources",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "more",
                table: "event_sources",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "read_ended_at",
                table: "event_sources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "read_started_at",
                table: "event_sources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "succeeded_at",
                table: "event_sources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_kind_key_gaps",
                table: "journal",
                columns: new[] { "kind", "key" },
                unique: true,
                filter: "kind = 'events_gap_observed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_journal_kind_key_gaps",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "failures",
                table: "event_sources");

            migrationBuilder.DropColumn(
                name: "last_class",
                table: "event_sources");

            migrationBuilder.DropColumn(
                name: "last_code",
                table: "event_sources");

            migrationBuilder.DropColumn(
                name: "more",
                table: "event_sources");

            migrationBuilder.DropColumn(
                name: "read_ended_at",
                table: "event_sources");

            migrationBuilder.DropColumn(
                name: "read_started_at",
                table: "event_sources");

            migrationBuilder.DropColumn(
                name: "succeeded_at",
                table: "event_sources");
        }
    }
}
