using Microsoft.AspNetCore.Builder;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Auth;

public static class WebApplicationAuthExtensions
{
    private const string DisableWebdavAuthEnvVar = "DISABLE_WEBDAV_AUTH";

    private const string DisabledWebdavAuthLog =
        "WebDAV authentication is DISABLED via the DISABLE_WEBDAV_AUTH environment variable";

    private static readonly Action<Action> LogOnlyOnce = DebounceUtil.RunOnlyOnce();

    public static bool IsWebdavAuthDisabled()
    {
        var isWebdavAuthDisabled = EnvironmentUtil.IsVariableTrue(DisableWebdavAuthEnvVar);
        if (isWebdavAuthDisabled) LogOnlyOnce(() => Log.Information(DisabledWebdavAuthLog));
        return isWebdavAuthDisabled;
    }

    public static void UseWebdavBasicAuthentication(this WebApplication app)
    {
        if (IsWebdavAuthDisabled()) return;
        app.UseAuthentication();
    }
}