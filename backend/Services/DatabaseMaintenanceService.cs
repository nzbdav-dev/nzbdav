using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Services;

public sealed class DatabaseMaintenanceService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConfigManager _configManager;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private Timer? _timer;

    public DatabaseMaintenanceService(IServiceScopeFactory scopeFactory, ConfigManager configManager)
    {
        _scopeFactory = scopeFactory;
        _configManager = configManager;
        var hours = EnvironmentUtil.GetLongVariable("DATABASE_MAINTENANCE_INTERVAL_HOURS") ?? 6;
        _interval = TimeSpan.FromHours(Math.Max(1, hours));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(_ => _ = RunAsync(), null, TimeSpan.Zero, _interval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    private async Task RunAsync()
    {
        if (!await _semaphore.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DavDatabaseContext>();
            var cancellationToken = SigtermUtil.GetCancellationToken();
            await DatabaseMaintenance.EnsureCompressedPayloadsAsync(dbContext, cancellationToken).ConfigureAwait(false);
            await DatabaseMaintenance.RunRetentionAsync(dbContext, _configManager, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Database maintenance run failed: {Message}", ex.Message);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _semaphore.Dispose();
    }
}
