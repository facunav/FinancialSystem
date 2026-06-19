using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateReconciledExpenseAgain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountDelta",
                table: "ReconciledExpenses",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "GroupingMode",
                table: "ReconciledExpenses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasAmountMismatch",
                table: "ReconciledExpenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountDelta",
                table: "ReconciledExpenses");

            migrationBuilder.DropColumn(
                name: "GroupingMode",
                table: "ReconciledExpenses");

            migrationBuilder.DropColumn(
                name: "HasAmountMismatch",
                table: "ReconciledExpenses");
        }
    }
}
