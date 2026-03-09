using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryItemIdToDavItemsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DavItems_Type_NextHealthCheck_ReleaseDate_Id",
                table: "DavItems");

            migrationBuilder.AddColumn<Guid>(
                name: "HistoryItemId",
                table: "DavItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HistoryCleanupItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoryCleanupItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DavItems_HistoryItemId",
                table: "DavItems",
                column: "HistoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DavItems_Type_HistoryItemId_NextHealthCheck_ReleaseDate_Id",
                table: "DavItems",
                columns: new[] { "Type", "HistoryItemId", "NextHealthCheck", "ReleaseDate", "Id" });

            migrationBuilder.Sql(
                """
                CREATE TRIGGER TR_HistoryItems_Delete_AddHistoryCleanup
                AFTER DELETE ON HistoryItems
                BEGIN
                    INSERT INTO HistoryCleanupItems (Id)
                    VALUES (OLD.Id);
                END
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_HistoryItems_Delete_AddHistoryCleanup");

            migrationBuilder.DropTable(
                name: "HistoryCleanupItems");

            migrationBuilder.DropIndex(
                name: "IX_DavItems_HistoryItemId",
                table: "DavItems");

            migrationBuilder.DropIndex(
                name: "IX_DavItems_Type_HistoryItemId_NextHealthCheck_ReleaseDate_Id",
                table: "DavItems");

            migrationBuilder.DropColumn(
                name: "HistoryItemId",
                table: "DavItems");

            migrationBuilder.CreateIndex(
                name: "IX_DavItems_Type_NextHealthCheck_ReleaseDate_Id",
                table: "DavItems",
                columns: new[] { "Type", "NextHealthCheck", "ReleaseDate", "Id" });
        }
    }
}
