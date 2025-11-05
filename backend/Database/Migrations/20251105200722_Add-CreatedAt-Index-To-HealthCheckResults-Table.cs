using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedAtIndexToHealthCheckResultsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_HealthCheckResults_CreatedAt",
                table: "HealthCheckResults",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HealthCheckResults_CreatedAt",
                table: "HealthCheckResults");
        }
    }
}
