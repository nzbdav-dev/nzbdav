using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class PopulateHealthCheckCategoriesSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // If api.ensure-article-existence is "true", populate api.ensure-article-existence-categories
            // by combining api.manual-category (default: "uncategorized") with api.categories
            // (default: "audio, software, tv, movies")
            migrationBuilder.Sql("""
                WITH RECURSIVE
                    settings AS (
                        SELECT
                            COALESCE(
                                (SELECT "ConfigValue" FROM "ConfigItems" WHERE "ConfigName" = 'api.ensure-article-existence'),
                                'false'
                            ) AS ensure_existence,
                            COALESCE(
                                (SELECT "ConfigValue" FROM "ConfigItems" WHERE "ConfigName" = 'api.categories'),
                                'audio, software, tv, movies'
                            ) AS categories,
                            COALESCE(
                                (SELECT "ConfigValue" FROM "ConfigItems" WHERE "ConfigName" = 'api.manual-category'),
                                'uncategorized'
                            ) AS manual_category
                    ),
                    -- Split categories into individual items (handling spaces after commas)
                    split_categories(item, rest) AS (
                        SELECT
                            '',
                            categories || ','
                        FROM settings
                        WHERE LOWER(ensure_existence) = 'true'
                        UNION ALL
                        SELECT
                            TRIM(SUBSTR(rest, 1, INSTR(rest, ',') - 1)),
                            SUBSTR(rest, INSTR(rest, ',') + 1)
                        FROM split_categories
                        WHERE rest <> ''
                    ),
                    -- Collect all non-empty category items
                    category_list AS (
                        SELECT item FROM split_categories WHERE item <> ''
                    ),
                    -- Combine manual_category with all categories
                    combined AS (
                        SELECT
                            TRIM(s.manual_category) || ', ' || GROUP_CONCAT(c.item, ', ') AS combined_categories
                        FROM settings s, category_list c
                        WHERE LOWER(s.ensure_existence) = 'true'
                    )
                INSERT INTO "ConfigItems" ("ConfigName", "ConfigValue")
                SELECT 'api.ensure-article-existence-categories', combined_categories
                FROM combined
                WHERE combined_categories IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "ConfigItems"
                      WHERE "ConfigName" = 'api.ensure-article-existence-categories'
                  );
                """);

            // Delete the old api.ensure-article-existence setting
            migrationBuilder.Sql("""
                DELETE FROM "ConfigItems"
                WHERE "ConfigName" = 'api.ensure-article-existence';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // If api.ensure-article-existence-categories exists and is non-empty,
            // then restore api.ensure-article-existence as "true"
            migrationBuilder.Sql("""
                INSERT INTO "ConfigItems" ("ConfigName", "ConfigValue")
                SELECT 'api.ensure-article-existence', 'true'
                WHERE EXISTS (
                    SELECT 1 FROM "ConfigItems"
                    WHERE "ConfigName" = 'api.ensure-article-existence-categories'
                      AND TRIM("ConfigValue") <> ''
                )
                AND NOT EXISTS (
                    SELECT 1 FROM "ConfigItems"
                    WHERE "ConfigName" = 'api.ensure-article-existence'
                );
                """);

            // Delete the categories setting
            migrationBuilder.Sql("""
                DELETE FROM "ConfigItems"
                WHERE "ConfigName" = 'api.ensure-article-existence-categories';
                """);
        }
    }
}
