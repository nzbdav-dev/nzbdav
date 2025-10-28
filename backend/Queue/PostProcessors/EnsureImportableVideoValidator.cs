using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Queue.PostProcessors;

public class EnsureImportableVideoValidator(DavDatabaseClient dbClient)
{
    public void ThrowIfValidationFails()
    {
        if (!IsValid())
        {
            throw new NoVideoFilesFoundException("No importable videos found.");
        }
    }

    private bool IsValid()
    {
        return dbClient.Ctx.ChangeTracker.Entries<DavItem>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(x => x.Type != DavItem.ItemType.Directory)
            .Any(x => FilenameUtil.IsVideoFile(x.Name));
    }
}