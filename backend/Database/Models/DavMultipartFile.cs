using NzbWebDAV.Models;

namespace NzbWebDAV.Database.Models;

public class DavMultipartFile
{
    public Guid Id { get; set; } // foreign key to DavItem.Id
    public Meta Metadata { get; set; }

    // navigation helpers
    public DavItem? DavItem { get; set; }

    public class Meta
    {
        public AesParams? AesParams { get; set; }
        public FilePart[] FileParts { get; set; } = [];
    }

    public class FilePart
    {
        // a subsequence of segments from an NzbFile
        public string[] SegmentIds { get; set; } = [];

        // what byte range is contained within the segmentIds? (relative to the full NzbFile)
        public LongRange SegmentIdByteRange { get; set; }

        // what byte range contains the file part contents? (relative to the full NzbFile)
        // note: this range should always be fully contained within the SegmentIdByteRange above.
        public LongRange FilePartByteRange { get; set; }
    }
}