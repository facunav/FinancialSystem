using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateManualExpenseTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DiscardedAt",
                table: "ManualExpenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDiscarded",
                table: "ManualExpenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ManualExpenses_IsDiscarded",
                table: "ManualExpenses",
                column: "IsDiscarded",
                filter: "\"IsDiscarded\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ManualExpenses_IsDiscarded",
                table: "ManualExpenses");

            migrationBuilder.DropColumn(
                name: "DiscardedAt",
                table: "ManualExpenses");

            migrationBuilder.DropColumn(
                name: "IsDiscarded",
                table: "ManualExpenses");
        }
    }
}
