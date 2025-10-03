using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFMpegCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NWebDav.Server;
using NWebDav.Server.Authentication;
using NWebDav.Server.Stores;
using NzbWebDAV.Api.SabControllers;
using NzbWebDAV.Clients;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Extensions;
using NzbWebDAV.Middlewares;
using NzbWebDAV.Queue;
using NzbWebDAV.Tasks;
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
        // Initialize logger
        var defaultLevel = LogEventLevel.Warning;
        var envLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");
        var level = Enum.TryParse<LogEventLevel>(envLevel, true, out var parsed) ? parsed : defaultLevel;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Mvc", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Error)
            .WriteTo.Console(theme: AnsiConsoleTheme.Code)
            .CreateLogger();

        // Configure FFMpegCore to use the ffprobe binary installed in the container
        GlobalFFOptions.Configure(new FFOptions { BinaryFolder = "/usr/bin" });

        // initialize database
        await using var databaseContext = new DavDatabaseContext();

        // run database migration, if necessary.
        if (args.Contains("--db-migration"))
        {
            Log.Information("Beginning database migration");
            try
            {
                // Clear any stale migration locks first
                Log.Information("Clearing any stale migration locks...");
                await databaseContext.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsLock WHERE 1=1;");
                Log.Information("Migration locks cleared");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not clear migration locks (table may not exist yet): {Message}", ex.Message);
            }

            Log.Information("Starting database migration with 60 second timeout...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                SigtermUtil.GetCancellationToken(),
                cts.Token);

            await databaseContext.Database.MigrateAsync(combinedCts.Token);
            return;
        }

        // initialize the config-manager
        var configManager = new ConfigManager();
        await configManager.LoadConfig();

        // initialize websocket-manager
        var websocketManager = new WebsocketManager();

        // initialize webapp
        var builder = WebApplication.CreateBuilder(args);
        var maxRequestBodySize = EnvironmentUtil.GetLongVariable("MAX_REQUEST_BODY_SIZE") ?? 100 * 1024 * 1024;
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBodySize);
        builder.Host.UseSerilog();
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });
        builder.Services.AddHealthChecks();
        builder.Services
            .AddSingleton(configManager)
            .AddSingleton(websocketManager)
            .AddSingleton<UsenetStreamingClient>()
            .AddSingleton<QueueManager>()
            .AddSingleton<ArrManager>()
            .AddSingleton<MediaIntegrityBackgroundScheduler>()
            .AddSingleton<MediaIntegrityService>()
            .AddScoped<DavDatabaseContext>()
            .AddScoped<DavDatabaseClient>()
            .AddScoped<DatabaseStore>()
            .AddScoped<IStore, DatabaseStore>()
            .AddScoped<GetAndHeadHandlerPatch>()
            .AddScoped<SabApiController>();


        builder.Services.AddNWebDav(opts =>
            {
                opts.Handlers["GET"] = typeof(GetAndHeadHandlerPatch);
                opts.Handlers["HEAD"] = typeof(GetAndHeadHandlerPatch);
                opts.Filter = opts.GetFilter();
                opts.RequireAuthentication = true;
            });

        // add basic auth
        builder.Services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Join(DavDatabaseContext.ConfigPath, "data-protection")));
        builder.Services
            .AddAuthentication(opts => opts.DefaultScheme = BasicAuthenticationDefaults.AuthenticationScheme)
            .AddBasicAuthentication(opts =>
            {
                opts.AllowInsecureProtocol = true;
                opts.CacheCookieName = "nzb-webdav-backend";
                opts.CacheCookieExpiration = TimeSpan.FromHours(1);
                opts.Events.OnValidateCredentials = (context) => ValidateCredentials(context, configManager);
            });

        // run
        var app = builder.Build();
        app.UseSerilogRequestLogging(options =>
        {
            // Reduce PROPFIND requests to DEBUG level to reduce log noise
            // Keep GET/POST and other requests at INFO level
            options.GetLevel = (httpContext, elapsed, ex) =>
            {
                if (ex != null)
                    return LogEventLevel.Error;

                var method = httpContext.Request.Method;
                if (method == "PROPFIND")
                    return LogEventLevel.Debug;

                // All other HTTP methods (GET, POST, PUT, DELETE, etc.) stay at INFO level
                return LogEventLevel.Information;
            };
        });
        app.UseMiddleware<ExceptionMiddleware>();
        app.UseWebSockets();
        app.MapHealthChecks("/health");
        app.Map("/ws", websocketManager.HandleRoute);
        app.MapControllers();
        app.UseAuthentication();
        app.UseNWebDav();

        // Force creation of MediaIntegrityBackgroundScheduler to start the background task
        _ = app.Services.GetRequiredService<MediaIntegrityBackgroundScheduler>();

        await app.RunAsync();
    }

    private static Task ValidateCredentials(ValidateCredentialsContext context, ConfigManager configManager)
    {
        var user = configManager.GetWebdavUser();
        var passwordHash = configManager.GetWebdavPasswordHash();

        if (user == null || passwordHash == null)
            context.Fail("webdav user and password are not yet configured.");

        if (context.Username == user && PasswordUtil.Verify(passwordHash!, context.Password))
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, context.Username, ClaimValueTypes.String,
                    context.Options.ClaimsIssuer),
                new Claim(ClaimTypes.Name, context.Username, ClaimValueTypes.String,
                    context.Options.ClaimsIssuer)
            };

            context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
            context.Success();
        }
        else
        {
            context.Fail("invalid credentials");
        }

        return Task.CompletedTask;
    }
}