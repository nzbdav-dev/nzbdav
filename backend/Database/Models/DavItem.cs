using System.Text.Json.Serialization;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Database.Models;

public class DavItem
{
    public const int IdPrefixLength = 5;

    public Guid Id { get; init; }
    public string IdPrefix { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid? ParentId { get; init; }
    public string Name { get; set; } = null!;
    public long? FileSize { get; set; }
    public ItemType Type { get; init; }
    public ItemSubType SubType { get; init; }
    public string Path { get; set; } = null!;
    public DateTimeOffset? ReleaseDate { get; set; }
    public DateTimeOffset? LastHealthCheck { get; set; }
    public DateTimeOffset? NextHealthCheck { get; set; }
    public Guid? HistoryItemId { get; set; }
    public Guid? FileBlobId { get; set; }
    public Guid? NzbBlobId { get; set; }

    public static DavItem New
    (
        Guid id,
        DavItem parent,
        string name,
        long? fileSize,
        ItemType type,
        ItemSubType subType,
        DateTimeOffset? releaseDate,
        DateTimeOffset? lastHealthCheck,
        Guid? historyItemId,
        Guid? fileBlobId,
        Guid? nzbBlobId = null
    )
    {
        return new DavItem()
        {
            Id = id,
            IdPrefix = id.GetFiveLengthPrefix(),
            CreatedAt = DateTime.Now,
            ParentId = parent.Id,
            Name = name,
            FileSize = fileSize,
            Type = type,
            SubType = subType,
            Path = System.IO.Path.Join(parent.Path, name),
            ReleaseDate = releaseDate,
            LastHealthCheck = lastHealthCheck,
            NextHealthCheck = releaseDate != null && lastHealthCheck != null
                ? releaseDate.Value + 2 * (lastHealthCheck.Value - releaseDate.Value)
                : null,
            HistoryItemId = historyItemId,
            FileBlobId = fileBlobId,
            NzbBlobId = nzbBlobId
        };
    }

    // Important: numerical values cannot be
    // changed without a database migration.
    public enum ItemType
    {
        Directory = 1,
        UsenetFile = 2,
    }

    public enum ItemSubType
    {
        // directory subtypes
        Directory = 101,
        WebdavRoot = 102,
        NzbsRoot = 103,
        ContentRoot = 104,
        SymlinkRoot = 105,
        IdsRoot = 106,

        // usenet file subtypes
        NzbFile = 201,
        RarFile = 202,
        MultipartFile = 203,
    }

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
        SubType = ItemSubType.WebdavRoot,
        Path = "/",
    };

    public static readonly DavItem NzbFolder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        ParentId = Root.Id,
        Name = "nzbs",
        FileSize = null,
        Type = ItemType.Directory,
        SubType = ItemSubType.NzbsRoot,
        Path = "/nzbs",
    };

    public static readonly DavItem ContentFolder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        ParentId = Root.Id,
        Name = "content",
        FileSize = null,
        Type = ItemType.Directory,
        SubType = ItemSubType.ContentRoot,
        Path = "/content",
    };

    public static readonly DavItem SymlinkFolder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
        ParentId = Root.Id,
        Name = "completed-symlinks",
        FileSize = null,
        Type = ItemType.Directory,
        SubType = ItemSubType.SymlinkRoot,
        Path = "/completed-symlinks",
    };

    public static readonly DavItem IdsFolder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
        ParentId = Root.Id,
        Name = ".ids",
        FileSize = null,
        Type = ItemType.Directory,
        SubType = ItemSubType.IdsRoot,
        Path = "/.ids",
    };
}