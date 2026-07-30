using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.UsenetMigrations
{
    /// <inheritdoc />
    public partial class AddMigrationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    AltmountMetadataRoot = table.Column<string>(type: "TEXT", nullable: true),
                    AltmountConfigPath = table.Column<string>(type: "TEXT", nullable: true),
                    AltmountStoreRoot = table.Column<string>(type: "TEXT", nullable: true),
                    MaxQueueDepth = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmitWorkers = table.Column<int>(type: "INTEGER", nullable: false),
                    SymlinkLibraryRoot = table.Column<string>(type: "TEXT", nullable: true),
                    SymlinkBackupDir = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationPreferences", x => x.Id);
                    table.CheckConstraint("CK_MigrationPreferences_Singleton", "Id = 1");
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "MigrationPreferences"
                    ("Id", "AltmountMetadataRoot", "AltmountConfigPath", "AltmountStoreRoot",
                     "MaxQueueDepth", "SubmitWorkers", "SymlinkLibraryRoot", "SymlinkBackupDir", "UpdatedAt")
                SELECT "Id", "AltmountMetadataRoot", "AltmountConfigPath", "AltmountStoreRoot",
                       "MaxQueueDepth", "SubmitWorkers", "SymlinkLibraryRoot", "SymlinkBackupDir", "UpdatedAt"
                FROM "SessionState"
                WHERE "Id" = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationPreferences");
        }
    }
}
