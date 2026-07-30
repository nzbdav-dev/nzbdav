using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.UsenetMigrations
{
    /// <inheritdoc />
    public partial class InitializeUsenetMigrationDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CategoryMap",
                columns: table => new
                {
                    AltmountCategory = table.Column<string>(type: "TEXT", nullable: false),
                    AltmountDir = table.Column<string>(type: "TEXT", nullable: true),
                    AltmountSanitizedDir = table.Column<string>(type: "TEXT", nullable: true),
                    AltmountType = table.Column<string>(type: "TEXT", nullable: true),
                    TargetCategory = table.Column<string>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    DiscoveredBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryMap", x => x.AltmountCategory);
                });

            migrationBuilder.CreateTable(
                name: "Releases",
                columns: table => new
                {
                    StoreRef = table.Column<string>(type: "TEXT", nullable: false),
                    StoreBasename = table.Column<string>(type: "TEXT", nullable: false),
                    QueueId = table.Column<long>(type: "INTEGER", nullable: true),
                    SubmitFileName = table.Column<string>(type: "TEXT", nullable: false),
                    QueueFileName = table.Column<string>(type: "TEXT", nullable: false),
                    JobName = table.Column<string>(type: "TEXT", nullable: false),
                    JobNameDiverges = table.Column<bool>(type: "INTEGER", nullable: false),
                    AltmountCategory = table.Column<string>(type: "TEXT", nullable: true),
                    TargetCategory = table.Column<string>(type: "TEXT", nullable: true),
                    Verdict = table.Column<string>(type: "TEXT", nullable: false),
                    VerdictReasons = table.Column<string>(type: "TEXT", nullable: false),
                    VerdictDetail = table.Column<string>(type: "TEXT", nullable: true),
                    CollisionGroupKey = table.Column<string>(type: "TEXT", nullable: true),
                    MetaFileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    NzbFileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SegmentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EstFetchBytesLazy = table.Column<long>(type: "INTEGER", nullable: false),
                    EstFetchBytesEager = table.Column<long>(type: "INTEGER", nullable: false),
                    IsRarRelease = table.Column<bool>(type: "INTEGER", nullable: false),
                    EstStatCommands = table.Column<long>(type: "INTEGER", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Encryption = table.Column<string>(type: "TEXT", nullable: true),
                    HasPassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasFilenamePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    WorstFileStatus = table.Column<string>(type: "TEXT", nullable: true),
                    HasNestedSources = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasClipBoundaries = table.Column<bool>(type: "INTEGER", nullable: false),
                    SourceNzbdavId = table.Column<string>(type: "TEXT", nullable: true),
                    Included = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Releases", x => x.StoreRef);
                });

            migrationBuilder.CreateTable(
                name: "ScanErrors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanErrors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    AltmountMetadataRoot = table.Column<string>(type: "TEXT", nullable: true),
                    AltmountConfigPath = table.Column<string>(type: "TEXT", nullable: true),
                    AltmountStoreRoot = table.Column<string>(type: "TEXT", nullable: true),
                    MaxQueueDepth = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmitWorkers = table.Column<int>(type: "INTEGER", nullable: false),
                    ScanLazyRarEnabled = table.Column<bool>(type: "INTEGER", nullable: true),
                    ScanWindowsSafePaths = table.Column<bool>(type: "INTEGER", nullable: true),
                    ScanStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScanCompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RunStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RunCompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionState", x => x.Id);
                    table.CheckConstraint("CK_SessionState_Singleton", "Id = 1");
                });

            migrationBuilder.CreateTable(
                name: "ReleaseFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreRef = table.Column<string>(type: "TEXT", nullable: false),
                    MetaPath = table.Column<string>(type: "TEXT", nullable: false),
                    VirtualPath = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    NormalisedName = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    FileStatus = table.Column<string>(type: "TEXT", nullable: true),
                    NzbdavId = table.Column<string>(type: "TEXT", nullable: true),
                    NewDavItemId = table.Column<string>(type: "TEXT", nullable: true),
                    Flags = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseFiles_Releases_StoreRef",
                        column: x => x.StoreRef,
                        principalTable: "Releases",
                        principalColumn: "StoreRef",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Submissions",
                columns: table => new
                {
                    StoreRef = table.Column<string>(type: "TEXT", nullable: false),
                    NzoId = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HistoryClearedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MountPath = table.Column<string>(type: "TEXT", nullable: true),
                    DavItemCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.StoreRef);
                    table.ForeignKey(
                        name: "FK_Submissions_Releases_StoreRef",
                        column: x => x.StoreRef,
                        principalTable: "Releases",
                        principalColumn: "StoreRef",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseFiles_NormalisedName",
                table: "ReleaseFiles",
                column: "NormalisedName");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseFiles_StoreRef",
                table: "ReleaseFiles",
                column: "StoreRef");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_CollisionGroupKey",
                table: "Releases",
                column: "CollisionGroupKey");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_JobName",
                table: "Releases",
                column: "JobName");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_TargetCategory",
                table: "Releases",
                column: "TargetCategory");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_TargetCategory_QueueFileName",
                table: "Releases",
                columns: new[] { "TargetCategory", "QueueFileName" });

            migrationBuilder.CreateIndex(
                name: "IX_Releases_Verdict_Included",
                table: "Releases",
                columns: new[] { "Verdict", "Included" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_State",
                table: "Submissions",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryMap");

            migrationBuilder.DropTable(
                name: "ReleaseFiles");

            migrationBuilder.DropTable(
                name: "ScanErrors");

            migrationBuilder.DropTable(
                name: "SessionState");

            migrationBuilder.DropTable(
                name: "Submissions");

            migrationBuilder.DropTable(
                name: "Releases");
        }
    }
}
