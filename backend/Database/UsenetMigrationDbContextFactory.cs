using Microsoft.EntityFrameworkCore.Design;

namespace NzbWebDAV.Database;

/// <summary>
/// Lets <c>dotnet-ef</c> create an
/// <see cref="UsenetMigrationDbContext"/> without bootstrapping the app host.
/// </summary>
public class UsenetMigrationDbContextFactory : IDesignTimeDbContextFactory<UsenetMigrationDbContext>
{
    public UsenetMigrationDbContext CreateDbContext(string[] args) => new();
}
