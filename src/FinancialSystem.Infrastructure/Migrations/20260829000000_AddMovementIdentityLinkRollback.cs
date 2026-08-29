using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMovementIdentityLinkRollback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MovementIdentityLinkRollbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    RolledBackBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RolledBackAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovementIdentityLinkRollbacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MovementIdentityLinkRollbackMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RollbackId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEntityType = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Classification = table.Column<int>(type: "integer", nullable: false),
                    Evidence = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    OriginalCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OriginalCreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovementIdentityLinkRollbackMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovementIdentityLinkRollbackMembers_MovementIdentityLinkRollbacks_RollbackId",
                        column: x => x.RollbackId,
                        principalTable: "MovementIdentityLinkRollbacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovementIdentityLinkRollbacks_IdentityGroupId_Unique",
                table: "MovementIdentityLinkRollbacks",
                column: "IdentityGroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovementIdentityLinkRollbackMembers_RollbackId",
                table: "MovementIdentityLinkRollbackMembers",
                column: "RollbackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovementIdentityLinkRollbackMembers");

            migrationBuilder.DropTable(
                name: "MovementIdentityLinkRollbacks");
        }
    }
}
