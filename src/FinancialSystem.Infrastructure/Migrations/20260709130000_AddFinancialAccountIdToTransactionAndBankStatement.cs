using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialAccountIdToTransactionAndBankStatement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FinancialAccountId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinancialAccountId",
                table: "BankStatements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_FinancialAccountId",
                table: "Transactions",
                column: "FinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_FinancialAccountId",
                table: "BankStatements",
                column: "FinancialAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_FinancialAccounts_FinancialAccountId",
                table: "Transactions",
                column: "FinancialAccountId",
                principalTable: "FinancialAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatements_FinancialAccounts_FinancialAccountId",
                table: "BankStatements",
                column: "FinancialAccountId",
                principalTable: "FinancialAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_FinancialAccounts_FinancialAccountId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_BankStatements_FinancialAccounts_FinancialAccountId",
                table: "BankStatements");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_FinancialAccountId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_BankStatements_FinancialAccountId",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "FinancialAccountId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "FinancialAccountId",
                table: "BankStatements");
        }
    }
}
