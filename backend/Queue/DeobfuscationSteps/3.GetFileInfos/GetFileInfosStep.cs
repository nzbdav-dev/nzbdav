using System.Security.Cryptography;
using NzbWebDAV.Extensions;
using NzbWebDAV.Par2Recovery.Packets;
using NzbWebDAV.Queue.DeobfuscationSteps._1.FetchFirstSegment;
using NzbWebDAV.Utils;
using Usenet.Nzb;

namespace NzbWebDAV.Queue.DeobfuscationSteps._3.GetFileInfos;

public static class GetFileInfosStep
{
    public static List<FileInfo> GetFileInfos
    (
        List<FetchFirstSegmentsStep.NzbFileWithFirstSegment> files,
        List<FileDesc> par2FileDescriptors
    )
    {
        using var md5 = MD5.Create();
        var hashToFileDescMap = GetHashToFileDescMap(par2FileDescriptors);
        var filesInfos = files
            .Select(x => GetFileInfo(x, hashToFileDescMap, md5))
            .ToList();

        return filesInfos;
    }

    private static Dictionary<string, FileDesc> GetHashToFileDescMap(List<FileDesc> par2FileDescriptors)
    {
        var hashToFileDescMap = new Dictionary<string, FileDesc>();
        foreach (var descriptor in par2FileDescriptors)
        {
            var hash = BitConverter.ToString(descriptor.File16kHash);
            hashToFileDescMap[hash] = descriptor;
        }

        return hashToFileDescMap;
    }

    private static FileInfo GetFileInfo(
        FetchFirstSegmentsStep.NzbFileWithFirstSegment file,
        Dictionary<string, FileDesc> hashToFilenameMap,
        MD5 md5
    )
    {
        var hash = !file.MissingFirstSegment ? BitConverter.ToString(md5.ComputeHash(file.First16KB!)) : "";
        var fileDesc = hashToFilenameMap.GetValueOrDefault(hash);
        var subjectFileName = file.NzbFile.GetSubjectFileName();
        var headerFileName = file.Header?.FileName ?? "";
        var par2FileName = fileDesc?.FileName ?? "";
        var filename = new List<(string? FileName, int Priority)>
        {
            (FileName: par2FileName, Priority: GetFilenamePriority(par2FileName, 3)),
            (FileName: subjectFileName, Priority: GetFilenamePriority(subjectFileName, 2)),
            (FileName: headerFileName, Priority: GetFilenamePriority(headerFileName, 1)),
        }.Where(x => x.FileName is not null).MaxBy(x => x.Priority).FileName ?? "";

        return new FileInfo()
        {
            NzbFile = file.NzbFile,
            FileName = filename,
            ReleaseDate = file.ReleaseDate,
            FileSize = (long?)fileDesc?.FileLength,
            IsRar = file.HasRar4Magic() || file.HasRar5Magic(),
        };
    }

    private static int GetFilenamePriority(string? filename, int startingPriority)
    {
        var priority = startingPriority;
        if (string.IsNullOrWhiteSpace(filename)) return priority - 5000;
        if (ObfuscationUtil.IsProbablyObfuscated(filename)) priority -= 1000;
        if (FilenameUtil.IsImportantFileType(filename)) priority += 50;
        if (Path.GetExtension(filename).TrimStart('.').Length is >= 2 and <= 4) priority += 10;
        return priority;
    }

    public class FileInfo
    {
        public required NzbFile NzbFile { get; init; }
        public required string FileName { get; init; }
        public required DateTimeOffset ReleaseDate { get; init; }
        public long? FileSize { get; init; }
        public bool IsRar { get; init; }
    }
}