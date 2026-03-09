using Microsoft.EntityFrameworkCore.Design;

namespace NzbWebDAV.Database;

/// <summary>
/// Allows EF Core design-time tools (dotnet-ef) to create a DavDatabaseContext
/// without bootstrapping the full application host (Program.cs).
/// </summary>
public class DavDatabaseContextFactory : IDesignTimeDbContextFactory<DavDatabaseContext>
{
    public DavDatabaseContext CreateDbContext(string[] args)
    {
        return new DavDatabaseContext();
    }
}
