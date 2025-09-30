namespace NzbWebDAV.Models;

public record LongRange(long StartInclusive, long EndExclusive)
{
    public long Count => EndExclusive - StartInclusive;

    public bool Contains(long value) =>
        value >= StartInclusive && value < EndExclusive;

    public bool Contains(LongRange range) =>
        range.StartInclusive >= StartInclusive && range.EndExclusive <= EndExclusive;

    public bool IsContainedWithin(LongRange range) =>
        range.Contains(this);

    public static LongRange FromStartAndSize(long startInclusive, long size) =>
        new(startInclusive, startInclusive + size);
}