using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTypeCreatedAtIndexToDavItemsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DavItems_HistoryItemId",
                table: "DavItems");

            migrationBuilder.CreateIndex(
                name: "IX_DavItems_HistoryItemId_Type_CreatedAt",
                table: "DavItems",
                columns: new[] { "HistoryItemId", "Type", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DavItems_HistoryItemId_Type_CreatedAt",
                table: "DavItems");

            migrationBuilder.CreateIndex(
                name: "IX_DavItems_HistoryItemId",
                table: "DavItems",
                column: "HistoryItemId");
        }
    }
}
