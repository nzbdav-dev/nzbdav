using System.Text.Json.Serialization;

namespace NzbWebDAV.Database.Models;

public class DavItem
{
    public const int IdPrefixLength = 5;

    public Guid Id { get; init; }
    public string IdPrefix { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid? ParentId { get; init; }
    public string Name { get; init; } = null!;
    public long? FileSize { get; set; }
    public ItemType Type { get; init; }
    public string Path { get; init; } = null!;
    public DateTimeOffset? ReleaseDate { get; set; }
    public DateTimeOffset? LastHealthCheck { get; set; }
    public DateTimeOffset? NextHealthCheck { get; set; }

    public static DavItem New
    (
        Guid id,
        DavItem parent,
        string name,
        long? fileSize,
        ItemType type,
        DateTimeOffset? releaseDate,
        DateTimeOffset? lastHealthCheck
    )
    {
        return new DavItem()
        {
            Id = id,
            IdPrefix = id.ToString()[..5],
            CreatedAt = DateTime.Now,
            ParentId = parent.Id,
            Name = name,
            FileSize = fileSize,
            Type = type,
            Path = System.IO.Path.Join(parent.Path, name),
            ReleaseDate = releaseDate,
            LastHealthCheck = lastHealthCheck,
            NextHealthCheck = releaseDate != null && lastHealthCheck != null
                ? releaseDate.Value + 2 * (lastHealthCheck.Value - releaseDate.Value)
                : null
        };
    }

    // Important: numerical values cannot be
    // changed without a database migration.
    public enum ItemType
    {
        Directory = 1,
        SymlinkRoot = 2,
        NzbFile = 3,
        RarFile = 4,
        IdsRoot = 5,
    }

    // navigation helpers
    [JsonIgnore]
    public DavItem? Parent { get; set; }

    [JsonIgnore]
    public ICollection<DavItem> Children { get; set; } = new List<DavItem>();

    // static instances
    // Important: assigned values cannot be
    // changed without a database migration.
    public static readonly DavItem Root = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000000"),
        ParentId = null,
        Name = "/",
        FileSize = null,
        Type = ItemType.Directory,
        Path = "/",
    };

    public static readonly DavItem NzbFolder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        ParentId = Root.Id,
        Name = "nzbs",
        FileSize = null,
        Type = ItemType.Directory,
        Path = "/nzbs",
    };

    public static readonly DavItem ContentFolder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        ParentId = Root.Id,
        Name = "content",
        FileSize = null,
        Type = ItemType.Directory,
        Path = "/content",
    };

    public static readonly DavItem SymlinkFolder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
        ParentId = Root.Id,
        Name = "completed-symlinks",
        FileSize = null,
        Type = ItemType.SymlinkRoot,
        Path = "/completed-symlinks",
    };

    public static readonly DavItem IdsFolder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
        ParentId = Root.Id,
        Name = ".ids",
        FileSize = null,
        Type = ItemType.IdsRoot,
        Path = "/.ids",
    };
}