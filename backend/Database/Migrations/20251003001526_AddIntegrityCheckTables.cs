using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrityCheckTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntegrityCheckRuns",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RunType = table.Column<int>(type: "INTEGER", nullable: false),
                    ScanDirectory = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MaxFilesToCheck = table.Column<int>(type: "INTEGER", nullable: false),
                    CorruptFileAction = table.Column<int>(type: "INTEGER", nullable: false),
                    Mp4DeepScan = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoMonitor = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnmonitorValidatedFiles = table.Column<bool>(type: "INTEGER", nullable: false),
                    DirectDeletionFallback = table.Column<bool>(type: "INTEGER", nullable: false),
                    NzbSegmentSamplePercentage = table.Column<int>(type: "INTEGER", nullable: false),
                    NzbSegmentThresholdPercentage = table.Column<int>(type: "INTEGER", nullable: false),
                    ValidFiles = table.Column<int>(type: "INTEGER", nullable: false),
                    CorruptFiles = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalFiles = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRunning = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentFile = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ProgressPercentage = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrityCheckRuns", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "IntegrityCheckFileResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FileId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    IsLibraryFile = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastChecked = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ActionTaken = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrityCheckFileResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrityCheckFileResults_IntegrityCheckRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "IntegrityCheckRuns",
                        principalColumn: "RunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrityCheckFileResults_FileId",
                table: "IntegrityCheckFileResults",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrityCheckFileResults_RunId",
                table: "IntegrityCheckFileResults",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrityCheckFileResults");

            migrationBuilder.DropTable(
                name: "IntegrityCheckRuns");
        }
    }
}
