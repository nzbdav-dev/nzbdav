using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class PopulateUsenetProvidersConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH existing AS (
                    SELECT
                        NULLIF(TRIM((SELECT "ConfigValue" FROM "ConfigItems" WHERE "ConfigName" = 'usenet.host')), '') AS host,
                        NULLIF(TRIM((SELECT "ConfigValue" FROM "ConfigItems" WHERE "ConfigName" = 'usenet.port')), '') AS port,
                        NULLIF(TRIM((SELECT "ConfigValue" FROM "ConfigItems" WHERE "ConfigName" = 'usenet.use-ssl')), '') AS use_ssl,
                        COALESCE((SELECT "ConfigValue" FROM "ConfigItems" WHERE "ConfigName" = 'usenet.user'), '') AS user,
                        COALESCE((SELECT "ConfigValue" FROM "ConfigItems" WHERE "ConfigName" = 'usenet.pass'), '') AS pass,
                        NULLIF(TRIM((SELECT "ConfigValue" FROM "ConfigItems" WHERE "ConfigName" = 'usenet.connections')), '') AS connections
                )
                INSERT INTO "ConfigItems" ("ConfigName", "ConfigValue")
                SELECT
                    'usenet.providers',
                    '{"Providers":[{"Type":1,"Host":"' ||
                        replace(replace(host, '\', '\\'), '"', '\"') ||
                        '","Port":' || CAST(COALESCE(port, '119') AS INTEGER) ||
                        ',"UseSsl":' ||
                        CASE
                            WHEN use_ssl IS NOT NULL AND LOWER(use_ssl) IN ('1','true','t','yes','y','on') THEN 'true'
                            ELSE 'false'
                        END ||
                        ',"User":"' || replace(replace(COALESCE(user, ''), '\', '\\'), '"', '\"') ||
                        '","Pass":"' || replace(replace(COALESCE(pass, ''), '\', '\\'), '"', '\"') ||
                        '","MaxConnections":' || CAST(COALESCE(connections, '50') AS INTEGER) ||
                        '}]}'
                FROM existing
                WHERE host IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "ConfigItems"
                      WHERE "ConfigName" = 'usenet.providers'
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "ConfigItems"
                WHERE "ConfigName" = 'usenet.providers';
                """);
        }
    }
}
