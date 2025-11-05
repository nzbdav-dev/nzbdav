using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddHealthCheckStatsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealthCheckStats",
                columns: table => new
                {
                    DateStartInclusive = table.Column<long>(type: "INTEGER", nullable: false),
                    DateEndExclusive = table.Column<long>(type: "INTEGER", nullable: false),
                    Result = table.Column<int>(type: "INTEGER", nullable: false),
                    RepairStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthCheckStats", x => new { x.DateStartInclusive, x.DateEndExclusive, x.Result, x.RepairStatus });
                });

            // Populate HealthCheckStats from HealthCheckResults
            // Group by day (truncated to beginning of day in UTC), Result, and RepairStatus
            migrationBuilder.Sql(
                """
                INSERT INTO HealthCheckStats (DateStartInclusive, DateEndExclusive, Result, RepairStatus, Count)
                SELECT
                    CAST(strftime('%s', date(datetime(CreatedAt, 'unixepoch')) || ' 00:00:00') AS INTEGER) AS DateStartInclusive,
                    CAST(strftime('%s', date(datetime(CreatedAt, 'unixepoch'), '+1 day') || ' 00:00:00') AS INTEGER) AS DateEndExclusive,
                    Result,
                    RepairStatus,
                    COUNT(*) AS Count
                FROM HealthCheckResults
                GROUP BY
                    CAST(strftime('%s', date(datetime(CreatedAt, 'unixepoch')) || ' 00:00:00') AS INTEGER),
                    CAST(strftime('%s', date(datetime(CreatedAt, 'unixepoch'), '+1 day') || ' 00:00:00') AS INTEGER),
                    Result,
                    RepairStatus
                """
            );

            // Create trigger to automatically update HealthCheckStats when new rows are added to HealthCheckResults
            migrationBuilder.Sql(
                """
                CREATE TRIGGER TR_HealthCheckResults_IncrementStats
                AFTER INSERT ON HealthCheckResults
                BEGIN
                    INSERT INTO HealthCheckStats (DateStartInclusive, DateEndExclusive, Result, RepairStatus, Count)
                    VALUES (
                        CAST(strftime('%s', date(datetime(NEW.CreatedAt, 'unixepoch')) || ' 00:00:00') AS INTEGER),
                        CAST(strftime('%s', date(datetime(NEW.CreatedAt, 'unixepoch'), '+1 day') || ' 00:00:00') AS INTEGER),
                        NEW.Result,
                        NEW.RepairStatus,
                        1
                    )
                    ON CONFLICT(DateStartInclusive, DateEndExclusive, Result, RepairStatus) DO UPDATE SET
                        Count = Count + 1;
                END
                """
            );

            // Create trigger to automatically update HealthCheckStats when rows are deleted from HealthCheckResults
            migrationBuilder.Sql(
                """
                CREATE TRIGGER TR_HealthCheckResults_DecrementStats
                AFTER DELETE ON HealthCheckResults
                BEGIN
                    UPDATE HealthCheckStats
                    SET Count = Count - 1
                    WHERE DateStartInclusive = CAST(strftime('%s', date(datetime(OLD.CreatedAt, 'unixepoch')) || ' 00:00:00') AS INTEGER)
                      AND DateEndExclusive = CAST(strftime('%s', date(datetime(OLD.CreatedAt, 'unixepoch'), '+1 day') || ' 00:00:00') AS INTEGER)
                      AND Result = OLD.Result
                      AND RepairStatus = OLD.RepairStatus;

                    DELETE FROM HealthCheckStats
                    WHERE DateStartInclusive = CAST(strftime('%s', date(datetime(OLD.CreatedAt, 'unixepoch')) || ' 00:00:00') AS INTEGER)
                      AND DateEndExclusive = CAST(strftime('%s', date(datetime(OLD.CreatedAt, 'unixepoch'), '+1 day') || ' 00:00:00') AS INTEGER)
                      AND Result = OLD.Result
                      AND RepairStatus = OLD.RepairStatus
                      AND Count <= 0;
                END
                """
            );

            // Create trigger to automatically update HealthCheckStats when rows are updated in HealthCheckResults
            // Only processes updates when Result or RepairStatus changes
            migrationBuilder.Sql(
                """
                CREATE TRIGGER TR_HealthCheckResults_UpdateStats
                AFTER UPDATE ON HealthCheckResults
                WHEN OLD.Result != NEW.Result OR OLD.RepairStatus != NEW.RepairStatus
                BEGIN
                    -- Decrement count for the old combination (using old date range)
                    UPDATE HealthCheckStats
                    SET Count = Count - 1
                    WHERE DateStartInclusive = CAST(strftime('%s', date(datetime(OLD.CreatedAt, 'unixepoch')) || ' 00:00:00') AS INTEGER)
                      AND DateEndExclusive = CAST(strftime('%s', date(datetime(OLD.CreatedAt, 'unixepoch'), '+1 day') || ' 00:00:00') AS INTEGER)
                      AND Result = OLD.Result
                      AND RepairStatus = OLD.RepairStatus;

                    -- Delete the old stat row if count reaches 0
                    DELETE FROM HealthCheckStats
                    WHERE DateStartInclusive = CAST(strftime('%s', date(datetime(OLD.CreatedAt, 'unixepoch')) || ' 00:00:00') AS INTEGER)
                      AND DateEndExclusive = CAST(strftime('%s', date(datetime(OLD.CreatedAt, 'unixepoch'), '+1 day') || ' 00:00:00') AS INTEGER)
                      AND Result = OLD.Result
                      AND RepairStatus = OLD.RepairStatus
                      AND Count <= 0;

                    -- Increment count for the new combination (using new date range, or old if CreatedAt didn't change)
                    -- If CreatedAt changed, we need to handle both old and new date ranges
                    -- First, handle the case where CreatedAt might have changed
                    INSERT INTO HealthCheckStats (DateStartInclusive, DateEndExclusive, Result, RepairStatus, Count)
                    VALUES (
                        CAST(strftime('%s', date(datetime(NEW.CreatedAt, 'unixepoch')) || ' 00:00:00') AS INTEGER),
                        CAST(strftime('%s', date(datetime(NEW.CreatedAt, 'unixepoch'), '+1 day') || ' 00:00:00') AS INTEGER),
                        NEW.Result,
                        NEW.RepairStatus,
                        1
                    )
                    ON CONFLICT(DateStartInclusive, DateEndExclusive, Result, RepairStatus) DO UPDATE SET
                        Count = Count + 1;
                END
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_HealthCheckResults_UpdateStats");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_HealthCheckResults_DecrementStats");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_HealthCheckResults_IncrementStats");

            migrationBuilder.DropTable(
                name: "HealthCheckStats");
        }
    }
}
