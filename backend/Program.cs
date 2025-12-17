using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NWebDav.Server;
using NWebDav.Server.Stores;
using NzbWebDAV.Api.SabControllers;
using NzbWebDAV.Auth;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Extensions;
using NzbWebDAV.Middlewares;
using NzbWebDAV.Queue;
using NzbWebDAV.Services;
using NzbWebDAV.Utils;
using NzbWebDAV.WebDav;
using NzbWebDAV.WebDav.Base;
using NzbWebDAV.Websocket;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace NzbWebDAV;

class Program
{
    static async Task Main(string[] args)
    {
        // Update thread-pool
        var coreCount = Environment.ProcessorCount;
        var minThreads = Math.Max(coreCount * 2, 50); // 2x cores, minimum 50
        var maxThreads = Math.Max(coreCount * 50, 1000); // 50x cores, minimum 1000
        ThreadPool.SetMinThreads(minThreads, minThreads);
        ThreadPool.SetMaxThreads(maxThreads, maxThreads);

        // Initialize logger
        var defaultLevel = LogEventLevel.Information;
        var envLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");
        var level = Enum.TryParse<LogEventLevel>(envLevel, true, out var parsed) ? parsed : defaultLevel;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("NWebDAV", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Mvc", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Error)
            .WriteTo.Console(theme: AnsiConsoleTheme.Code)
            .CreateLogger();

        var argsList = args.ToList();
        var ct = SigtermUtil.GetCancellationToken();

        if (await TryHandleCliCommandsAsync(argsList, ct).ConfigureAwait(false))
        {
            return;
        }

        await RunWebAppAsync(args).ConfigureAwait(false);
    }

    private static async Task RunWebAppAsync(string[] args)
    {
        var configManager = new ConfigManager();
        await configManager.LoadConfig().ConfigureAwait(false);

        // run one-off maintenance/compaction if requested
        // initialize websocket-manager
        var websocketManager = new WebsocketManager();

        // initialize webapp
        var builder = WebApplication.CreateBuilder(args);
        var maxRequestBodySize = EnvironmentUtil.GetLongVariable("MAX_REQUEST_BODY_SIZE") ?? 100 * 1024 * 1024;
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBodySize);
        builder.Host.UseSerilog();
        builder.Services.AddControllers();
        builder.Services.AddHealthChecks();
        builder.Services
            .AddWebdavBasicAuthentication(configManager)
            .AddSingleton(configManager)
            .AddSingleton(websocketManager)
            .AddSingleton<UsenetStreamingClient>()
            .AddSingleton<QueueManager>()
            .AddSingleton<ArrMonitoringService>()
            .AddSingleton<HealthCheckService>()
            .AddHostedService<DatabaseMaintenanceService>()
            .AddScoped<DavDatabaseContext>()
            .AddScoped<DavDatabaseClient>()
            .AddScoped<DatabaseStore>()
            .AddScoped<IStore, DatabaseStore>()
            .AddScoped<GetAndHeadHandlerPatch>()
            .AddScoped<SabApiController>()
            .AddNWebDav(opts =>
            {
                opts.Handlers["GET"] = typeof(GetAndHeadHandlerPatch);
                opts.Handlers["HEAD"] = typeof(GetAndHeadHandlerPatch);
                opts.Filter = opts.GetFilter();
                opts.RequireAuthentication = !WebApplicationAuthExtensions
                    .IsWebdavAuthDisabled();
            });

        // force instantiation of services
        var app = builder.Build();
        app.Services.GetRequiredService<ArrMonitoringService>();
        app.Services.GetRequiredService<HealthCheckService>();

        // run
        app.UseMiddleware<ExceptionMiddleware>();
        app.UseWebSockets();
        app.MapHealthChecks("/health");
        app.Map("/ws", websocketManager.HandleRoute);
        app.MapControllers();
        app.UseWebdavBasicAuthentication();
        app.UseNWebDav();
        app.Lifetime.ApplicationStopping.Register(SigtermUtil.Cancel);
        await app.RunAsync().ConfigureAwait(false);
    }

    private static async Task<bool> TryHandleCliCommandsAsync(IReadOnlyList<string> argsList, CancellationToken ct)
    {
        if (HasOption(argsList, "--version"))
        {
            var informationalVersion = typeof(Program)
                .Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            var assemblyVersion = typeof(Program).Assembly.GetName().Version?.ToString();
            var versionText = informationalVersion ?? assemblyVersion ?? "unknown";
            Console.WriteLine(versionText);
            return true;
        }

        if (HasOption(argsList, "--db-migration"))
        {
            var targetMigration = GetOption(argsList, "--db-migration");
            await using var migrationContext = new DavDatabaseContext();
            await migrationContext.Database.MigrateAsync(targetMigration, ct).ConfigureAwait(false);
            return true;
        }

        if (HasOption(argsList, "--export-db"))
        {
            var exportDir = GetOption(argsList, "--export-db");
            if (string.IsNullOrWhiteSpace(exportDir))
            {
                Log.Error("--export-db requires a target directory argument.");
                return true;
            }

            exportDir = Path.GetFullPath(exportDir);
            await using var exportContext = new DavDatabaseContext();
            var dumpService = new DatabaseDumpService();
            await dumpService.ExportAsync(exportContext, exportDir, ct).ConfigureAwait(false);
            return true;
        }

        if (HasOption(argsList, "--import-db"))
        {
            var importDir = GetOption(argsList, "--import-db");
            if (string.IsNullOrWhiteSpace(importDir))
            {
                Log.Error("--import-db requires a source directory argument.");
                return true;
            }

            importDir = Path.GetFullPath(importDir);
            await using var importContext = new DavDatabaseContext();
            var dumpService = new DatabaseDumpService();
            await dumpService.ImportAsync(importContext, importDir, ct).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private static string? GetOption(IReadOnlyList<string> args, string optionName)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == optionName && i + 1 < args.Count)
                return args[i + 1];

            if (arg.StartsWith(optionName + "=", StringComparison.Ordinal))
                return arg[(optionName.Length + 1)..];
        }

        return null;
    }

    private static bool HasOption(IReadOnlyList<string> args, string optionName)
    {
        foreach (var arg in args)
        {
            if (arg == optionName) return true;
            if (arg.StartsWith(optionName + "=", StringComparison.Ordinal)) return true;
        }

        return false;
    }
}