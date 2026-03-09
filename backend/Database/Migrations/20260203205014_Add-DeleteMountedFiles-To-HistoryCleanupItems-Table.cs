using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDeleteMountedFilesToHistoryCleanupItemsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_HistoryItems_Delete_AddHistoryCleanup;");
            
            migrationBuilder.AddColumn<bool>(
                name: "DeleteMountedFiles",
                table: "HistoryCleanupItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeleteMountedFiles",
                table: "HistoryCleanupItems");
            
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
    }
}
