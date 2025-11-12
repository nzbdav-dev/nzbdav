using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Queue.PostProcessors;

public class RenameDuplicatesPostProcessor(DavDatabaseClient dbClient)
{
    public void RenameDuplicates()
    {
        var groupsOfDuplicates = dbClient.Ctx.ChangeTracker.Entries<DavItem>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(x => x.Type != DavItem.ItemType.Directory)
            .GroupBy(x => new DuplicateKey(x.ParentId!.Value, x.Name))
            .Select(g => g.ToList())
            .Where(g => g.Count > 1);

        foreach (var group in groupsOfDuplicates)
            RenameDuplicatesInGroup(group);
    }

    private void RenameDuplicatesInGroup(List<DavItem> duplicates)
    {
        var canonicalName = Path.GetFileNameWithoutExtension(duplicates[0].Name);
        var canonicalExtension = Path.GetExtension(duplicates[0].Name);
        for (var i = 1; i < duplicates.Count; i++)
        {
            var newName = $"{canonicalName} ({i+1}){canonicalExtension}";
            var parentPath = Path.GetDirectoryName(duplicates[i].Path);
            duplicates[i].Name = newName;
            duplicates[i].Path = Path.Join(parentPath, newName);
        }
    }

    private record struct DuplicateKey(Guid ParentId, string Name);
}