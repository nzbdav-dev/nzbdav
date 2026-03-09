using MemoryPack;

namespace NzbWebDAV.Database.Models;

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DavNzbFile
{
    [MemoryPackOrder(0)]
    public Guid Id { get; set; } // foreign key to DavItem.Id

    [MemoryPackOrder(1)]
    public string[] SegmentIds { get; set; } = [];

    // navigation helpers
    [MemoryPackIgnore]
    public DavItem? DavItem { get; set; }
}