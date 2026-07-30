using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.UsenetMigrations
{
    /// <inheritdoc />
    public partial class AddMigrationProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CurrentRunId",
                table: "SessionState",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MigratedReleases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceType = table.Column<string>(type: "TEXT", nullable: false),
                    SourceReleaseId = table.Column<string>(type: "TEXT", nullable: false),
                    FirstRunId = table.Column<long>(type: "INTEGER", nullable: false),
                    LastRunId = table.Column<long>(type: "INTEGER", nullable: false),
                    NzoId = table.Column<string>(type: "TEXT", nullable: true),
                    TargetCategory = table.Column<string>(type: "TEXT", nullable: true),
                    JobName = table.Column<string>(type: "TEXT", nullable: true),
                    MountPath = table.Column<string>(type: "TEXT", nullable: true),
                    ExpectedFileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MappedFileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MigratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastVerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigratedReleases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MigrationRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceType = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MigratedFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MigratedReleaseId = table.Column<long>(type: "INTEGER", nullable: false),
                    VirtualPath = table.Column<string>(type: "TEXT", nullable: false),
                    NormalisedRelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    NormalisedName = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    DavItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NzbBlobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MatchMethod = table.Column<string>(type: "TEXT", nullable: false),
                    LastVerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigratedFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigratedFiles_MigratedReleases_MigratedReleaseId",
                        column: x => x.MigratedReleaseId,
                        principalTable: "MigratedReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigratedFiles_DavItemId",
                table: "MigratedFiles",
                column: "DavItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MigratedFiles_MigratedReleaseId_VirtualPath",
                table: "MigratedFiles",
                columns: new[] { "MigratedReleaseId", "VirtualPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigratedFiles_NzbBlobId",
                table: "MigratedFiles",
                column: "NzbBlobId");

            migrationBuilder.CreateIndex(
                name: "IX_MigratedReleases_LastRunId",
                table: "MigratedReleases",
                column: "LastRunId");

            migrationBuilder.CreateIndex(
                name: "IX_MigratedReleases_NzoId",
                table: "MigratedReleases",
                column: "NzoId");

            migrationBuilder.CreateIndex(
                name: "IX_MigratedReleases_SourceType_SourceReleaseId",
                table: "MigratedReleases",
                columns: new[] { "SourceType", "SourceReleaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationRuns_StartedAt",
                table: "MigrationRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigratedFiles");

            migrationBuilder.DropTable(
                name: "MigrationRuns");

            migrationBuilder.DropTable(
                name: "MigratedReleases");

            migrationBuilder.DropColumn(
                name: "CurrentRunId",
                table: "SessionState");
        }
    }
}
