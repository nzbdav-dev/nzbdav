using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTriggerToDavItemsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create trigger to automatically add a BlobCleanupItem when a DavItem with a FileBlobId is deleted
            migrationBuilder.Sql(
                """
                CREATE TRIGGER TR_DavItems_Delete_AddBlobCleanup
                AFTER DELETE ON DavItems
                WHEN OLD.FileBlobId IS NOT NULL
                BEGIN
                    INSERT INTO BlobCleanupItems (Id)
                    VALUES (OLD.FileBlobId);
                END
                """
            );

            // Create trigger to automatically add a BlobCleanupItem when a DavItem's FileBlobId is updated
            migrationBuilder.Sql(
                """
                CREATE TRIGGER TR_DavItems_Update_AddBlobCleanup
                AFTER UPDATE OF FileBlobId ON DavItems
                WHEN OLD.FileBlobId IS NOT NULL AND OLD.FileBlobId != NEW.FileBlobId
                BEGIN
                    INSERT INTO BlobCleanupItems (Id)
                    VALUES (OLD.FileBlobId);
                END
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_DavItems_Delete_AddBlobCleanup");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_DavItems_Update_AddBlobCleanup");
        }
    }
}
