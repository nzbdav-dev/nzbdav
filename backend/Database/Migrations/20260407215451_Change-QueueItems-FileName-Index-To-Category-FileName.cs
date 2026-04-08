using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class ChangeQueueItemsFileNameIndexToCategoryFileName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueueItems_FileName",
                table: "QueueItems");

            migrationBuilder.CreateIndex(
                name: "IX_QueueItems_Category_FileName",
                table: "QueueItems",
                columns: new[] { "Category", "FileName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueueItems_Category_FileName",
                table: "QueueItems");

            migrationBuilder.CreateIndex(
                name: "IX_QueueItems_FileName",
                table: "QueueItems",
                column: "FileName",
                unique: true);
        }
    }
}
