using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddHealthCheckQueueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DavItems_Type_NextHealthCheck_ReleaseDate_Id",
                table: "DavItems",
                columns: new[] { "Type", "NextHealthCheck", "ReleaseDate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DavItems_Type_NextHealthCheck_ReleaseDate_Id",
                table: "DavItems");
        }
    }
}
