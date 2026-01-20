using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTriggerToQueueItemsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create trigger to automatically add a BlobCleanupItem when a QueueItem is deleted
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_QueueItems_AddBlobCleanup");
        }
    }
}
