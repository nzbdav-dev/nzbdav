using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NzbWebDAV.Database.MigrationHelpers;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDavCleanupItemsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DavItems_DavItems_ParentId",
                table: "DavItems");

            migrationBuilder.CreateTable(
                name: "DavCleanupItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DavCleanupItems", x => x.Id);
                });

            // Recreate triggers destroyed by DropForeignKey's table rebuild (runs AFTER EF Core operations)
            // And add new `TR_DavItems_DeleteDirectory` trigger.
            migrationBuilder.SqlAfter(
                """
                CREATE TRIGGER TR_DavItems_Delete_AddBlobCleanup
                AFTER DELETE ON DavItems
                WHEN OLD.FileBlobId IS NOT NULL
                BEGIN
                    INSERT INTO BlobCleanupItems (Id)
                    VALUES (OLD.FileBlobId);
                END;

                CREATE TRIGGER TR_DavItems_Update_AddBlobCleanup
                AFTER UPDATE OF FileBlobId ON DavItems
                WHEN OLD.FileBlobId IS NOT NULL AND OLD.FileBlobId != NEW.FileBlobId
                BEGIN
                    INSERT INTO BlobCleanupItems (Id)
                    VALUES (OLD.FileBlobId);
                END;

                CREATE TRIGGER TR_DavItems_DeleteDirectory
                AFTER DELETE ON DavItems
                WHEN OLD.SubType = 101
                BEGIN
                    INSERT INTO DavCleanupItems (Id)
                    VALUES (OLD.Id);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_DavItems_DeleteDirectory;");

            migrationBuilder.DropTable(
                name: "DavCleanupItems");

            migrationBuilder.AddForeignKey(
                name: "FK_DavItems_DavItems_ParentId",
                table: "DavItems",
                column: "ParentId",
                principalTable: "DavItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Recreate triggers destroyed by AddForeignKey's table rebuild (runs AFTER EF Core operations)
            migrationBuilder.SqlAfter(
                """
                CREATE TRIGGER TR_DavItems_Delete_AddBlobCleanup
                AFTER DELETE ON DavItems
                WHEN OLD.FileBlobId IS NOT NULL
                BEGIN
                    INSERT INTO BlobCleanupItems (Id)
                    VALUES (OLD.FileBlobId);
                END;

                CREATE TRIGGER TR_DavItems_Update_AddBlobCleanup
                AFTER UPDATE OF FileBlobId ON DavItems
                WHEN OLD.FileBlobId IS NOT NULL AND OLD.FileBlobId != NEW.FileBlobId
                BEGIN
                    INSERT INTO BlobCleanupItems (Id)
                    VALUES (OLD.FileBlobId);
                END;
                """);
        }
    }
}
