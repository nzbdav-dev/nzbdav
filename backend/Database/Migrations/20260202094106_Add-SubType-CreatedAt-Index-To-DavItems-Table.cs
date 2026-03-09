using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSubTypeCreatedAtIndexToDavItemsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DavItems_HistoryItemId_SubType_CreatedAt",
                table: "DavItems",
                columns: new[] { "HistoryItemId", "SubType", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DavItems_HistoryItemId_SubType_CreatedAt",
                table: "DavItems");
        }
    }
}
