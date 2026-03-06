using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Tasks;

public class RecreateStrmFilesTask(
    ConfigManager configManager,
    DavDatabaseClient dbClient,
    WebsocketManager websocketManager
) : BaseTask(websocketManager, WebsocketTopic.RecreateStrmFilesTaskProgress)
{
    public async Task RecreateStrmFiles()
    {
        Report("Collecting all strm file candidates...");

        var files = dbClient.Ctx.Items
            .Where(x => x.Type != DavItem.ItemType.Directory)
            .AsAsyncEnumerable()
            .Where(x => FilenameUtil.IsVideoFile(x.Name));
        var fileCount = await files.CountAsync();
        var progress = 0;

        await foreach (var file in files)
        {
            ReportDebounced($"Creating strm file {++progress} / {fileCount}.");
            await StrmFileUtil.CreateStrmFileAsync(configManager, file);
        }

        Report($"Done. Created {fileCount} strm files.");
    }

    protected override async Task ExecuteInternal()
    {
        try
        {
            await RecreateStrmFiles().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Report($"Failed: {e.Message}");
            Log.Error(e, "Failed to recreate strm files.");
        }
    }
}