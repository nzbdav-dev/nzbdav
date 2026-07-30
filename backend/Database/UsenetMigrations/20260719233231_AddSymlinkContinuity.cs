using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.UsenetMigrations
{
    /// <inheritdoc />
    public partial class AddSymlinkContinuity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SymlinkBackupDir",
                table: "SessionState",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SymlinkLibraryRoot",
                table: "SessionState",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SymlinkRewrites",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SymlinkPath = table.Column<string>(type: "TEXT", nullable: false),
                    OldTarget = table.Column<string>(type: "TEXT", nullable: false),
                    NewTarget = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    MatchMethod = table.Column<string>(type: "TEXT", nullable: true),
                    StoreRef = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SymlinkRewrites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SymlinkRewrites_Status",
                table: "SymlinkRewrites",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SymlinkRewrites");

            migrationBuilder.DropColumn(
                name: "SymlinkBackupDir",
                table: "SessionState");

            migrationBuilder.DropColumn(
                name: "SymlinkLibraryRoot",
                table: "SessionState");
        }
    }
}
