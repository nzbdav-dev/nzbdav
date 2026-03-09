using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddNzbBlobIdAndNzbNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NzbBlobId",
                table: "HistoryItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NzbBlobId",
                table: "DavItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NzbBlobCleanupItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzbBlobCleanupItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NzbNames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzbNames", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoryItems_NzbBlobId",
                table: "HistoryItems",
                column: "NzbBlobId");

            migrationBuilder.CreateIndex(
                name: "IX_DavItems_NzbBlobId",
                table: "DavItems",
                column: "NzbBlobId");

            // Replace the old QueueItems trigger (which inserted into BlobCleanupItems)
            // with a new one that inserts into NzbBlobCleanupItems instead.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_QueueItems_AddBlobCleanup");

            migrationBuilder.Sql(
                """
                CREATE TRIGGER TR_QueueItems_AddNzbBlobCleanup
                AFTER DELETE ON QueueItems
                BEGIN
                    INSERT OR IGNORE INTO NzbBlobCleanupItems (Id)
                    VALUES (OLD.Id);
                END
                """
            );

            // When a HistoryItem is deleted, schedule its NZB blob for cleanup.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER TR_HistoryItems_Delete_AddNzbBlobCleanup
                AFTER DELETE ON HistoryItems
                WHEN OLD.NzbBlobId IS NOT NULL
                BEGIN
                    INSERT OR IGNORE INTO NzbBlobCleanupItems (Id)
                    VALUES (OLD.NzbBlobId);
                END
                """
            );

            // When a DavItem is deleted, schedule its NZB blob for cleanup.
            // INSERT OR IGNORE handles the case where multiple DavItems share the
            // same NzbBlobId (all files from the same download job).
            migrationBuilder.Sql(
                """
                CREATE TRIGGER TR_DavItems_Delete_AddNzbBlobCleanup
                AFTER DELETE ON DavItems
                WHEN OLD.NzbBlobId IS NOT NULL
                BEGIN
                    INSERT OR IGNORE INTO NzbBlobCleanupItems (Id)
                    VALUES (OLD.NzbBlobId);
                END
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_DavItems_Delete_AddNzbBlobCleanup");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_HistoryItems_Delete_AddNzbBlobCleanup");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_QueueItems_AddNzbBlobCleanup");

            // Restore the original QueueItems trigger
            migrationBuilder.Sql(
                """
                CREATE TRIGGER TR_QueueItems_AddBlobCleanup
                AFTER DELETE ON QueueItems
                BEGIN
                    INSERT INTO BlobCleanupItems (Id)
                    VALUES (OLD.Id);
                END
                """
            );

            migrationBuilder.DropTable(
                name: "NzbBlobCleanupItems");

            migrationBuilder.DropTable(
                name: "NzbNames");

            migrationBuilder.DropIndex(
                name: "IX_HistoryItems_NzbBlobId",
                table: "HistoryItems");

            migrationBuilder.DropIndex(
                name: "IX_DavItems_NzbBlobId",
                table: "DavItems");

            migrationBuilder.DropColumn(
                name: "NzbBlobId",
                table: "HistoryItems");

            migrationBuilder.DropColumn(
                name: "NzbBlobId",
                table: "DavItems");
        }
    }
}
