using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportConsistencyVerification : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "ConsistencyVerified", table: "ImportBatches", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ConsistencyIssues", table: "ImportBatches", type: "character varying(2000)", maxLength: 2000, nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ConsistencyVerified", table: "ImportBatches");
            migrationBuilder.DropColumn(name: "ConsistencyIssues", table: "ImportBatches");
        }
    }
}
