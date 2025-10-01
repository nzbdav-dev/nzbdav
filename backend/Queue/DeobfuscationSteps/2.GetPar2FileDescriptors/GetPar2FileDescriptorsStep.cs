using NzbWebDAV.Clients;
using NzbWebDAV.Extensions;
using NzbWebDAV.Par2Recovery;
using NzbWebDAV.Par2Recovery.Packets;
using NzbWebDAV.Queue.DeobfuscationSteps._1.FetchFirstSegment;

namespace NzbWebDAV.Queue.DeobfuscationSteps._2.GetPar2FileDescriptors;

public static class GetPar2FileDescriptorsStep
{
    public static async Task<List<FileDesc>> GetPar2FileDescriptors
    (
        List<FetchFirstSegmentsStep.NzbFileWithFirstSegment> files,
        UsenetStreamingClient client,
        CancellationToken cancellationToken = default
    )
    {
        // find the par2 index file
        var par2Index = files
            .Where(x => !x.MissingFirstSegment)
            .Where(x => Par2.IsPar2Index(x.First16KB!))
            .MinBy(x => x.NzbFile.Segments.Count);
        if (par2Index is null) return [];

        // return all file descriptors
        var fileDescriptors = new List<FileDesc>();
        var segments = par2Index.NzbFile.GetSegmentIds();
        var filesize = par2Index.NzbFile.Segments.Count == 1
            ? par2Index.Header!.PartOffset + par2Index.Header!.PartSize
            : await client.GetFileSizeAsync(par2Index.NzbFile, cancellationToken);
        await using var stream = client.GetFileStream(segments, filesize, concurrentConnections: 1);
        await foreach (var fileDescriptor in Par2.ReadFileDescriptions(stream, cancellationToken))
            fileDescriptors.Add(fileDescriptor);
        return fileDescriptors;
    }
}