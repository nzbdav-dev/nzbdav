using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPathToDavItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Path",
                table: "DavItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            BuildFullPath(migrationBuilder);
        }

        public static void BuildFullPath(MigrationBuilder migrationBuilder)
        {
            // Populate the Path column for every existing DavItem
            // * The root DavItem is given path `/`
            // * Every other DavItem is given path `{PARENT_PATH}/{NAME}`
            migrationBuilder.Sql(
                """
                WITH RECURSIVE computed(Id, Path) AS (
                    -- base case: the root item
                    SELECT Id, '/'
                    FROM DavItems
                    WHERE Id = '00000000-0000-0000-0000-000000000000'

                    UNION ALL

                    -- recursive case: all other items
                    SELECT
                        d.Id,
                        CASE
                            WHEN c.Path = '/' THEN '/' || d.Name
                            ELSE c.Path || '/' || d.Name
                        END AS Path
                    FROM DavItems d
                    JOIN computed c ON d.ParentId = c.Id
                )

                UPDATE DavItems
                SET Path = (SELECT Path FROM computed WHERE DavItems.Id = computed.Id);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Path",
                table: "DavItems");
        }
    }
}
