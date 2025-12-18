using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;

namespace NzbWebDAV.Database.Interceptors;

public class SqliteForeignKeyEnabler : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        try
        {
            using var command = connection.CreateCommand();

            // Always try to enable foreign keys; if the database is read-only
            // this may still succeed as it is a connection-level pragma.
            command.CommandText = "PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();

            // Best-effort: try to enable WAL and set synchronous level. These
            // may attempt writes to the DB file; if the connection string
            // explicitly requests read-only mode, skip attempting PRAGMA
            // writes to avoid "attempt to write a readonly database".
            var connStr = connection.ConnectionString ?? string.Empty;
            var isExplicitlyReadOnly = connStr.IndexOf("mode=readonly", StringComparison.OrdinalIgnoreCase) >= 0
                                     || connStr.IndexOf("mode=read-only", StringComparison.OrdinalIgnoreCase) >= 0
                                     || connStr.IndexOf("mode=ReadOnly", StringComparison.OrdinalIgnoreCase) >= 0
                                     || connStr.IndexOf("Mode=ReadOnly", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isExplicitlyReadOnly)
            {
                try
                {
                    command.CommandText = "PRAGMA journal_mode = WAL;";
                    _ = command.ExecuteScalar();

                    command.CommandText = "PRAGMA synchronous = NORMAL;";
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not set WAL/synchronous PRAGMA on SQLite connection; database may be read-only or on a read-only filesystem.");
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning(e, "SQLite connection opened but PRAGMA commands failed. Continuing without PRAGMA changes.");
        }
    }
}