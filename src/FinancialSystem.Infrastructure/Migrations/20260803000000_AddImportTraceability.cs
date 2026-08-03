using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportTraceability : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(name: "FileSizeBytes", table: "ImportBatches", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ParserUsed", table: "ImportBatches", type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<int>(name: "Outcome", table: "ImportBatches", type: "integer", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "FileSizeBytes", table: "ImportBatches");
            migrationBuilder.DropColumn(name: "ParserUsed", table: "ImportBatches");
            migrationBuilder.DropColumn(name: "Outcome", table: "ImportBatches");
        }
    }
}
