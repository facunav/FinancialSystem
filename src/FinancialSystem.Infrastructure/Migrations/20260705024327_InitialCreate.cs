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
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsDeactivated = table.Column<bool>(type: "boolean", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                });

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
                name: "CounterParties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DefaultCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultMovementType = table.Column<int>(type: "integer", nullable: true),
                    DefaultFinancialImpact = table.Column<int>(type: "integer", nullable: true),
                    IsDeactivated = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CounterParties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CounterParties_Categories_DefaultCategoryId",
                        column: x => x.DefaultCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassifiedMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MovementType = table.Column<int>(type: "integer", nullable: false),
                    FinancialImpact = table.Column<int>(type: "integer", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProcessingSource = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MatchScore = table.Column<double>(type: "double precision", nullable: true),
                    AmountDelta = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassifiedMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassifiedMovements_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassifiedMovements_CounterParties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalTable: "CounterParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassifiedMovementItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassifiedMovementId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEntityType = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OriginalDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    OriginalSourceFile = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassifiedMovementItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassifiedMovementItems_ClassifiedMovements_ClassifiedMovem~",
                        column: x => x.ClassifiedMovementId,
                        principalTable: "ClassifiedMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "IX_Categories_ParentId",
                table: "Categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "UX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassifiedMovementItems_ClassifiedMovementId",
                table: "ClassifiedMovementItems",
                column: "ClassifiedMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassifiedMovementItems_ClassifiedMovementId_Role",
                table: "ClassifiedMovementItems",
                columns: new[] { "ClassifiedMovementId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassifiedMovementItems_SourceEntityType_SourceId",
                table: "ClassifiedMovementItems",
                columns: new[] { "SourceEntityType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassifiedMovements_CategoryId",
                table: "ClassifiedMovements",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassifiedMovements_CounterpartyId",
                table: "ClassifiedMovements",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassifiedMovements_CounterpartyId_FinancialImpact",
                table: "ClassifiedMovements",
                columns: new[] { "CounterpartyId", "FinancialImpact" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassifiedMovements_EffectiveDate",
                table: "ClassifiedMovements",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_ClassifiedMovements_EffectiveDate_CategoryId",
                table: "ClassifiedMovements",
                columns: new[] { "EffectiveDate", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassifiedMovements_FinancialImpact",
                table: "ClassifiedMovements",
                column: "FinancialImpact");

            migrationBuilder.CreateIndex(
                name: "IX_ClassifiedMovements_MovementType",
                table: "ClassifiedMovements",
                column: "MovementType");

            migrationBuilder.CreateIndex(
                name: "IX_CounterParties_DefaultCategoryId",
                table: "CounterParties",
                column: "DefaultCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CounterParties_IsDeactivated",
                table: "CounterParties",
                column: "IsDeactivated");

            migrationBuilder.CreateIndex(
                name: "IX_CounterParties_IsDeactivated_Name",
                table: "CounterParties",
                columns: new[] { "IsDeactivated", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_CounterParties_Name",
                table: "CounterParties",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_CounterParties_Type",
                table: "CounterParties",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_CounterParties_Type_Name",
                table: "CounterParties",
                columns: new[] { "Type", "Name" });

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
                name: "ClassifiedMovementItems");

            migrationBuilder.DropTable(
                name: "LegacyImportedExpenses");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "ClassifiedMovements");

            migrationBuilder.DropTable(
                name: "CounterParties");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
