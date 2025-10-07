using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NWebDav.Server;
using NWebDav.Server.Authentication;
using NWebDav.Server.Stores;
using NzbWebDAV.Api.SabControllers;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Extensions;
using NzbWebDAV.Middlewares;
using NzbWebDAV.Queue;
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

        // initialize database
        await using var databaseContext = new DavDatabaseContext();

        // run database migration, if necessary.
        if (args.Contains("--db-migration"))
        {
            await databaseContext.Database.MigrateAsync(SigtermUtil.GetCancellationToken());
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
        builder.Services.AddControllers();
        builder.Services.AddHealthChecks();
        builder.Services
            .AddSingleton(configManager)
            .AddSingleton(websocketManager)
            .AddSingleton<UsenetStreamingClient>()
            .AddSingleton<QueueManager>()
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
        app.UseSerilogRequestLogging();
        app.UseMiddleware<ExceptionMiddleware>();
        app.UseWebSockets();
        app.MapHealthChecks("/health");
        app.Map("/ws", websocketManager.HandleRoute);
        app.MapControllers();
        app.UseAuthentication();
        app.UseNWebDav();
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