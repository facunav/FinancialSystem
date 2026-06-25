using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Concept = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Detail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "ARS"),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    BankName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceFile = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SheetName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RowNumber = table.Column<int>(type: "integer", nullable: true),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManualExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "ARS"),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    MonthLabel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PaymentStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Sheet = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceFile = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SheetName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RowNumber = table.Column<int>(type: "integer", nullable: true),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualExpenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    CouponNumber = table.Column<string>(type: "text", nullable: true),
                    RawLine = table.Column<string>(type: "text", nullable: true),
                    SourceFile = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "ARS"),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialImpact = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProcessingSource = table.Column<int>(type: "integer", nullable: false),
                    ReviewReason = table.Column<int>(type: "integer", nullable: true),
                    ReviewNotes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    MatchScore = table.Column<double>(type: "double precision", nullable: true),
                    AmountDelta = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessedExpenses_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedExpenseItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEntityType = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OriginalDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OriginalCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "ARS"),
                    OriginalSourceFile = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedExpenseItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessedExpenseItems_ProcessedExpenses_ProcessedExpenseId",
                        column: x => x.ProcessedExpenseId,
                        principalTable: "ProcessedExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_Date",
                table: "BankStatements",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_Date_Bank",
                table: "BankStatements",
                columns: new[] { "Date", "BankName" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_ExternalId",
                table: "BankStatements",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManualExpenses_Date",
                table: "ManualExpenses",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ManualExpenses_Date_Sheet",
                table: "ManualExpenses",
                columns: new[] { "Date", "Sheet" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualExpenses_ExternalId",
                table: "ManualExpenses",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedExpenseItems_ProcessedExpenseId",
                table: "ProcessedExpenseItems",
                column: "ProcessedExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedExpenseItems_Source",
                table: "ProcessedExpenseItems",
                columns: new[] { "SourceEntityType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_ProcessedExpenseItems_UniqueSourcePerExpense",
                table: "ProcessedExpenseItems",
                columns: new[] { "ProcessedExpenseId", "SourceEntityType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedExpenses_CategoryId",
                table: "ProcessedExpenses",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedExpenses_Date_Category_Impact",
                table: "ProcessedExpenses",
                columns: new[] { "EffectiveDate", "CategoryId", "FinancialImpact" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedExpenses_EffectiveDate",
                table: "ProcessedExpenses",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedExpenses_FinancialImpact",
                table: "ProcessedExpenses",
                column: "FinancialImpact");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedExpenses_ProcessedBy_At",
                table: "ProcessedExpenses",
                columns: new[] { "ProcessedBy", "ProcessedAt" },
                filter: "\"ProcessedBy\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedExpenses_Status",
                table: "ProcessedExpenses",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Date",
                table: "Transactions",
                column: "Date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankStatements");

            migrationBuilder.DropTable(
                name: "ManualExpenses");

            migrationBuilder.DropTable(
                name: "ProcessedExpenseItems");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "ProcessedExpenses");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
