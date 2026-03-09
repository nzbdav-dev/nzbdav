using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDavItemsTypeAndSubType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubType",
                table: "DavItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Migrate old Type values to new Type + SubType:
            // Old types: Directory=1, SymlinkRoot=2, NzbFile=3, RarFile=4, IdsRoot=5, MultipartFile=6
            // New types: Directory=1, UsenetFile=2
            // New subtypes: Directory=101, WebdavRoot=102, NzbsRoot=103, ContentRoot=104,
            //               SymlinkRoot=105, IdsRoot=106, NzbFile=201, RarFile=202, MultipartFile=203

            // Directory (Type=1) -> SubType=101 (Directory)
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET SubType = 101
                WHERE Type = 1;
                """
            );

            // SymlinkRoot (Type=2) -> Type=1 (Directory), SubType=105 (SymlinkRoot)
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET Type = 1, SubType = 105
                WHERE Type = 2;
                """
            );

            // NzbFile (Type=3) -> Type=2 (UsenetFile), SubType=201 (NzbFile)
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET Type = 2, SubType = 201
                WHERE Type = 3;
                """
            );

            // RarFile (Type=4) -> Type=2 (UsenetFile), SubType=202 (RarFile)
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET Type = 2, SubType = 202
                WHERE Type = 4;
                """
            );

            // IdsRoot (Type=5) -> Type=1 (Directory), SubType=106 (IdsRoot)
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET Type = 1, SubType = 106
                WHERE Type = 5;
                """
            );

            // MultipartFile (Type=6) -> Type=2 (UsenetFile), SubType=203 (MultipartFile)
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET Type = 2, SubType = 203
                WHERE Type = 6;
                """
            );

            // Update well-known folders with their specific SubTypes
            // Root: 00000000-0000-0000-0000-000000000000 -> SubType=102 (WebdavRoot)
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET SubType = 102
                WHERE Id = '00000000-0000-0000-0000-000000000000';
                """
            );

            // NzbFolder: 00000000-0000-0000-0000-000000000001 -> SubType=103 (NzbsRoot)
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET SubType = 103
                WHERE Id = '00000000-0000-0000-0000-000000000001';
                """
            );

            // ContentFolder: 00000000-0000-0000-0000-000000000002 -> SubType=104 (ContentRoot)
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET SubType = 104
                WHERE Id = '00000000-0000-0000-0000-000000000002';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert SubType-based types back to old Type values
            // NzbFile (Type=2, SubType=201) -> Type=3
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET Type = 3
                WHERE Type = 2 AND SubType = 201;
                """
            );

            // RarFile (Type=2, SubType=202) -> Type=4
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET Type = 4
                WHERE Type = 2 AND SubType = 202;
                """
            );

            // MultipartFile (Type=2, SubType=203) -> Type=6
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET Type = 6
                WHERE Type = 2 AND SubType = 203;
                """
            );

            // SymlinkRoot (Type=1, SubType=105) -> Type=2
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET Type = 2
                WHERE Type = 1 AND SubType = 105;
                """
            );

            // IdsRoot (Type=1, SubType=106) -> Type=5
            migrationBuilder.Sql(
                """
                UPDATE DavItems
                SET Type = 5
                WHERE Type = 1 AND SubType = 106;
                """
            );

            migrationBuilder.DropColumn(
                name: "SubType",
                table: "DavItems");
        }
    }
}
