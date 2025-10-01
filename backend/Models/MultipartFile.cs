using Usenet.Nzb;

namespace NzbWebDAV.Models;

public class MultipartFile
{
    public required List<FilePart> FileParts { get; init; }
    public long FileSize => FileParts.Last().ByteRange.EndExclusive;

    public class FilePart
    {
        public required NzbFile NzbFile { get; init; }
        public required LongRange ByteRange { get; init; }
        public long PartSize => ByteRange.Count;
    }
}