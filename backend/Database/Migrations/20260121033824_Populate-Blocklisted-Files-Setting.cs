using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class PopulateBlocklistedFilesSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Transform api.download-extension-blacklist (e.g., ".nfo, .par2, .sfv")
            // into api.download-file-blocklist (e.g., "*.nfo, *.par2, *.sfv")
            migrationBuilder.Sql("""
                WITH RECURSIVE
                    source AS (
                        SELECT "ConfigValue" AS val
                        FROM "ConfigItems"
                        WHERE "ConfigName" = 'api.download-extension-blacklist'
                    ),
                    split(item, rest) AS (
                        SELECT
                            '',
                            val || ','
                        FROM source
                        WHERE val IS NOT NULL
                        UNION ALL
                        SELECT
                            TRIM(SUBSTR(rest, 1, INSTR(rest, ',') - 1)),
                            SUBSTR(rest, INSTR(rest, ',') + 1)
                        FROM split
                        WHERE rest <> ''
                    ),
                    transformed AS (
                        SELECT
                            GROUP_CONCAT('*' || item, ', ') AS new_val
                        FROM split
                        WHERE item <> ''
                    )
                INSERT INTO "ConfigItems" ("ConfigName", "ConfigValue")
                SELECT 'api.download-file-blocklist', new_val
                FROM transformed
                WHERE new_val IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "ConfigItems"
                      WHERE "ConfigName" = 'api.download-file-blocklist'
                  );
                """);

            migrationBuilder.Sql("""
                DELETE FROM "ConfigItems"
                WHERE "ConfigName" = 'api.download-extension-blacklist';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Transform api.download-file-blocklist back to api.download-extension-blacklist
            // Only include items that can be cleanly converted (pattern: *.ext where ext has no wildcards)
            migrationBuilder.Sql("""
                WITH RECURSIVE
                    source AS (
                        SELECT "ConfigValue" AS val
                        FROM "ConfigItems"
                        WHERE "ConfigName" = 'api.download-file-blocklist'
                    ),
                    split(item, rest) AS (
                        SELECT
                            '',
                            val || ','
                        FROM source
                        WHERE val IS NOT NULL
                        UNION ALL
                        SELECT
                            TRIM(SUBSTR(rest, 1, INSTR(rest, ',') - 1)),
                            SUBSTR(rest, INSTR(rest, ',') + 1)
                        FROM split
                        WHERE rest <> ''
                    ),
                    cleaned AS (
                        SELECT
                            SUBSTR(item, 2) AS ext  -- Remove leading '*', leaving '.ext'
                        FROM split
                        WHERE item <> ''
                          AND item LIKE '*.%'  -- Must start with *.
                          AND SUBSTR(item, 3) NOT LIKE '%*%'  -- Extension part has no *
                          AND SUBSTR(item, 3) NOT LIKE '%?%'  -- Extension part has no ?
                    ),
                    transformed AS (
                        SELECT
                            GROUP_CONCAT(ext, ', ') AS old_val
                        FROM cleaned
                    )
                INSERT INTO "ConfigItems" ("ConfigName", "ConfigValue")
                SELECT 'api.download-extension-blacklist', old_val
                FROM transformed
                WHERE old_val IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "ConfigItems"
                      WHERE "ConfigName" = 'api.download-extension-blacklist'
                  );
                """);

            migrationBuilder.Sql("""
                DELETE FROM "ConfigItems"
                WHERE "ConfigName" = 'api.download-file-blocklist';
                """);
        }
    }
}
