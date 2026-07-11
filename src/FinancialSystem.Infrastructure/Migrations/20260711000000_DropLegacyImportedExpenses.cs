using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyImportedExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegacyImportedExpenses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegacyImportedExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Sheet = table.Column<int>(type: "integer", nullable: false),
                    MonthLabel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PaymentStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDiscarded = table.Column<bool>(type: "boolean", nullable: false),
                    DiscardedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceFile = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SheetName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RowNumber = table.Column<int>(type: "integer", nullable: true),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyImportedExpenses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportedExpenses_Amount",
                table: "LegacyImportedExpenses",
                column: "Amount");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportedExpenses_Date",
                table: "LegacyImportedExpenses",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportedExpenses_Date_Amount",
                table: "LegacyImportedExpenses",
                columns: new[] { "Date", "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportedExpenses_ExternalId",
                table: "LegacyImportedExpenses",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportedExpenses_IsDiscarded",
                table: "LegacyImportedExpenses",
                column: "IsDiscarded");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportedExpenses_IsDiscarded_Amount",
                table: "LegacyImportedExpenses",
                columns: new[] { "IsDiscarded", "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportedExpenses_IsDiscarded_Date",
                table: "LegacyImportedExpenses",
                columns: new[] { "IsDiscarded", "Date" });
        }
    }
}
