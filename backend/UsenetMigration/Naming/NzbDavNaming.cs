using NzbWebDAV.Api.SabControllers.AddFile;
using NzbWebDAV.Utils;

namespace NzbWebDAV.UsenetMigration.Naming;

/// <summary>
/// Delegates migration naming to NzbDAV's normal submission transforms. Queue
/// identity and predicted mount paths therefore follow the same rules as regular
/// SAB submissions, including cases where <c>basename != JobName</c>. Because the
/// migration runs in the same assembly, it can call
/// <see cref="AddFileRequest.ResolveFileName"/> and
/// <see cref="FilenameUtil.GetJobName"/> directly and automatically inherits
/// changes to either transform.
/// </summary>
public static class NzbDavNaming
{
    /// <summary>
    /// The value that lands in <c>QueueItem.FileName</c> — half of the
    /// <c>UNIQUE(Category, FileName)</c> key. Equivalent to submitting
    /// <paramref name="nzbBasename"/> as the SAB <c>nzbname</c> param.
    /// </summary>
    public static string QueueFileName(string nzbBasename) =>
        AddFileRequest.ResolveFileName(nzbBasename, null);

    /// <summary>
    /// The predicted mount folder: <c>/content/{category}/{JobName}/</c>.
    /// </summary>
    public static string JobName(string nzbBasename) =>
        FilenameUtil.GetJobName(QueueFileName(nzbBasename));
}
