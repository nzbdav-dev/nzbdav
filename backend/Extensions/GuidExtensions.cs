// ReSharper disable InconsistentNaming

namespace NzbWebDAV.Extensions;

public static class GuidExtensions
{
    public static string GetFiveLengthPrefix(this Guid guid)
    {
        return guid.ToString()[..5];
    }
}