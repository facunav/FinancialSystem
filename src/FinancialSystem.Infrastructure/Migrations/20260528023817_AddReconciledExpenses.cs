using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciledExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReconciledExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "ARS"),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MatchScore = table.Column<double>(type: "double precision", nullable: false),
                    MatchConfidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciledExpenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciledExpenseItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReconciledExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEntityType = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OriginalDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OriginalCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "ARS"),
                    OriginalSourceFile = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ContributionScore = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciledExpenseItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciledExpenseItems_ReconciledExpenses_ReconciledExpense~",
                        column: x => x.ReconciledExpenseId,
                        principalTable: "ReconciledExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciledExpenseItems_ReconciledExpenseId",
                table: "ReconciledExpenseItems",
                column: "ReconciledExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciledExpenseItems_Source",
                table: "ReconciledExpenseItems",
                columns: new[] { "SourceEntityType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_ReconciledExpenseItems_UniqueSourcePerExpense",
                table: "ReconciledExpenseItems",
                columns: new[] { "ReconciledExpenseId", "SourceEntityType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciledExpenses_ConfirmedBy_ConfirmedAt",
                table: "ReconciledExpenses",
                columns: new[] { "ConfirmedBy", "ConfirmedAt" },
                filter: "\"ConfirmedBy\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciledExpenses_EffectiveDate",
                table: "ReconciledExpenses",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciledExpenses_Period",
                table: "ReconciledExpenses",
                columns: new[] { "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciledExpenses_Status",
                table: "ReconciledExpenses",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciledExpenses_Status_PeriodStart",
                table: "ReconciledExpenses",
                columns: new[] { "Status", "PeriodStart" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciledExpenseItems");

            migrationBuilder.DropTable(
                name: "ReconciledExpenses");
        }
    }
}
