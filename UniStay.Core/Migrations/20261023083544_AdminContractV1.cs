using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UniStay.Core.Migrations
{
    /// <inheritdoc />
    public partial class AdminContractV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "photos",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DateJoined",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            // Existing records have no historical creation timestamp; use migration time.
            // Keep only explicitly mapped permissions; removed capabilities are not retained.
            migrationBuilder.Sql("""
                UPDATE accounts SET "Permissions" = ARRAY(
                    SELECT DISTINCT CASE p
                        WHEN 'users.view' THEN 'users.read' WHEN 'users.change' THEN 'users.update'
                        WHEN 'profiles.view' THEN 'profiles.read' WHEN 'profiles.change' THEN 'profiles.update'
                        WHEN 'listings.view' THEN 'listings.read' WHEN 'photos.view' THEN 'photos.read'
                        WHEN 'applications.view' THEN 'applications.read' WHEN 'events.view' THEN 'application_events.read'
                        ELSE p END
                    FROM unnest("Permissions") p
                    WHERE p NOT IN ('users.add', 'users.delete'));
                """);
            migrationBuilder.CreateTable(
                name: "admin_audit_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorId = table.Column<long>(type: "bigint", nullable: false),
                    TargetUserId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ChangedFields = table.Column<string[]>(type: "text[]", nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: false),
                    After = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_audit_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_admin_audit_events_accounts_ActorId",
                        column: x => x.ActorId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admin_audit_events_accounts_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_events_ActorId",
                table: "admin_audit_events",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_events_TargetUserId",
                table: "admin_audit_events",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_audit_events");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "DateJoined",
                table: "accounts");
        }
    }
}
