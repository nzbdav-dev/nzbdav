using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.UsenetMigration.Proto;

namespace NzbWebDAV.Tests.UsenetMigration;

public class MetadataReaderTests
{
    [Fact]
    public void ReadFileMetadata_V3WithMagic_DecodesConsumedFields()
    {
        var payload = new TestProtoWriter()
            .Varint(1, 123456)                    // file_size
            .Varint(3, 1)                          // status = HEALTHY
            .String(6, "s3cr$t")                   // password
            .String(7, "saltysalt")                // salt
            .Varint(8, 1)                          // encryption = RCLONE
            .Varint(12, 1700000000)                // release_date
            .Bytes(15, new byte[] { 0x08, 0x01 })  // nested_sources (a sub-message)
            .Bytes(17, new byte[] { 0x08, 0x02 })  // clip_boundaries
            .String(18, "/config/.nzbs/tv/9-Show.nzbz") // store_ref
            .ToArray();

        var withMagic = MetadataReader.MetaMagicV3.Concat(payload).ToArray();
        var fm = MetadataReader.ReadFileMetadata(withMagic);

        Assert.True(fm.IsV3);
        Assert.Equal(123456, fm.FileSize);
        Assert.Equal(AltmountFileStatus.Healthy, fm.Status);
        Assert.Equal("s3cr$t", fm.Password);
        Assert.Equal("saltysalt", fm.Salt);
        Assert.Equal(AltmountEncryption.Rclone, fm.Encryption);
        Assert.Equal(1700000000, fm.ReleaseDate);
        Assert.True(fm.HasNestedSources);
        Assert.True(fm.HasClipBoundaries);
        Assert.Equal("/config/.nzbs/tv/9-Show.nzbz", fm.StoreRef);
    }

    [Fact]
    public void ReadFileMetadata_V1NoMagic_HasEmptyStoreRef()
    {
        // v1 metadata is a raw proto with no store_ref, producing Red no_store_ref.
        var payload = new TestProtoWriter()
            .Varint(1, 42)
            .Varint(3, 1)
            .ToArray();

        var fm = MetadataReader.ReadFileMetadata(payload);
        Assert.False(fm.IsV3);
        Assert.Equal("", fm.StoreRef);
    }

    [Fact]
    public void ReadFileMetadata_SkipsUnknownFields()
    {
        // Unknown fields (e.g. a future proto addition) must be skipped by wire type.
        var payload = new TestProtoWriter()
            .Varint(1, 7)
            .Fixed32(99, 0xDEADBEEF)                 // unknown fixed32
            .String(50, "some future string field")  // unknown length-delimited
            .Varint(60, 12345)                        // unknown varint
            .String(18, "/config/.nzbs/x/1-a.nzbz")
            .ToArray();

        var fm = MetadataReader.ReadFileMetadata(payload);
        Assert.Equal(7, fm.FileSize);
        Assert.Equal("/config/.nzbs/x/1-a.nzbz", fm.StoreRef);
    }

    [Fact]
    public void ReadStoreRef_FastPath_MatchesFullDecode()
    {
        var payload = new TestProtoWriter().String(18, "/config/.nzbs/tv/5-Show.nzbz").ToArray();
        var withMagic = MetadataReader.MetaMagicV3.Concat(payload).ToArray();
        Assert.Equal("/config/.nzbs/tv/5-Show.nzbz", MetadataReader.ReadStoreRef(withMagic));
    }

    [Fact]
    public void ReadNzbStore_DecodesFilesGroupsAndSegments()
    {
        var seg1 = new TestProtoWriter().String(1, "msg-id-1@x").Varint(2, 1).Varint(3, 700_000).ToArray();
        var seg2 = new TestProtoWriter().String(1, "msg-id-2@x").Varint(2, 2).Varint(3, 650_000).ToArray();
        var file = new TestProtoWriter()
            .String(1, "Subject [1/2] - \"file.rar\"")
            .String(2, "poster@example.com")
            .Varint(3, 1699999999)
            .String(4, "alt.binaries.test")
            .Bytes(5, seg1)
            .Bytes(5, seg2)
            .ToArray();
        var store = new TestProtoWriter().Bytes(1, file).ToArray();

        var decoded = MetadataReader.ReadNzbStore(store);
        Assert.Single(decoded.Files);
        var f = decoded.Files[0];
        Assert.Equal("Subject [1/2] - \"file.rar\"", f.Subject);
        Assert.Equal("poster@example.com", f.Poster);
        Assert.Equal(1699999999, f.Date);
        Assert.Equal(new[] { "alt.binaries.test" }, f.Groups);
        Assert.Equal(2, f.Segments.Count);
        Assert.Equal("msg-id-1@x", f.Segments[0].Id);
        Assert.Equal(1, f.Segments[0].Number);
        Assert.Equal(700_000, f.Segments[0].Bytes);
        Assert.Equal(650_000, f.Segments[1].Bytes);
    }
}
