using MemoryPack;

namespace NzbWebDAV.Models;

[MemoryPackable(GenerateType.VersionTolerant)]
public partial record LongRange
{
    [MemoryPackOrder(0)]
    public long StartInclusive { get; set; }

    [MemoryPackOrder(1)]
    public long EndExclusive { get; set; }

    [MemoryPackIgnore]
    public long Count => EndExclusive - StartInclusive;

    public LongRange(long startInclusive, long endExclusive)
    {
        StartInclusive = startInclusive;
        EndExclusive = endExclusive;
    }

    public bool Contains(long value) =>
        value >= StartInclusive && value < EndExclusive;

    public bool Contains(LongRange range) =>
        range.StartInclusive >= StartInclusive && range.EndExclusive <= EndExclusive;

    public bool IsContainedWithin(LongRange range) =>
        range.Contains(this);

    public static LongRange FromStartAndSize(long startInclusive, long size) =>
        new(startInclusive, startInclusive + size);
}