using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Config;

public class ConfigManager
{
    private readonly Dictionary<string, string> _config = new();
    public event EventHandler<ConfigEventArgs>? OnConfigChanged;

    public async Task LoadConfig()
    {
        await using var dbContext = new DavDatabaseContext();
        var configItems = await dbContext.ConfigItems.ToListAsync();
        lock (_config)
        {
            _config.Clear();
            foreach (var configItem in configItems)
            {
                _config[configItem.ConfigName] = configItem.ConfigValue;
            }
        }
    }

    public string? GetConfigValue(string configName)
    {
        lock (_config)
        {
            return _config.TryGetValue(configName, out string? value) ? value : null;
        }
    }

    public void UpdateValues(List<ConfigItem> configItems)
    {
        lock (_config)
        {
            foreach (var configItem in configItems)
            {
                _config[configItem.ConfigName] = configItem.ConfigValue;
            }

            OnConfigChanged?.Invoke(this, new ConfigEventArgs
            {
                ChangedConfig = configItems.ToDictionary(x => x.ConfigName, x => x.ConfigValue),
                NewConfig = _config
            });
        }
    }

    public string GetRcloneMountDir()
    {
        return StringUtil.EmptyToNull(GetConfigValue("rclone.mount-dir"))
               ?? StringUtil.EmptyToNull(Environment.GetEnvironmentVariable("MOUNT_DIR"))
               ?? "/tmp";
    }

    public string GetApiKey()
    {
        return StringUtil.EmptyToNull(GetConfigValue("api.key"))
               ?? EnvironmentUtil.GetVariable("FRONTEND_BACKEND_API_KEY");
    }

    public string GetApiCategories()
    {
        return StringUtil.EmptyToNull(GetConfigValue("api.categories"))
               ?? StringUtil.EmptyToNull(Environment.GetEnvironmentVariable("CATEGORIES"))
               ?? "audio,software,tv,movies";
    }

    public int GetMaxConnections()
    {
        return int.Parse(
            StringUtil.EmptyToNull(GetConfigValue("usenet.connections"))
            ?? "10"
        );
    }

    public int GetConnectionsPerStream()
    {
        return int.Parse(
            StringUtil.EmptyToNull(GetConfigValue("usenet.connections-per-stream"))
            ?? StringUtil.EmptyToNull(Environment.GetEnvironmentVariable("CONNECTIONS_PER_STREAM"))
            ?? "1"
        );
    }

    public string? GetWebdavUser()
    {
        return StringUtil.EmptyToNull(GetConfigValue("webdav.user"))
               ?? StringUtil.EmptyToNull(Environment.GetEnvironmentVariable("WEBDAV_USER"));
    }

    public string? GetWebdavPasswordHash()
    {
        var hashedPass = StringUtil.EmptyToNull(GetConfigValue("webdav.pass"));
        if (hashedPass != null) return hashedPass;
        var pass = Environment.GetEnvironmentVariable("WEBDAV_PASSWORD");
        if (pass != null) return PasswordUtil.Hash(pass);
        return null;
    }

    public bool IsEnsureImportableVideoEnabled()
    {
        var defaultValue = true;
        var configValue = StringUtil.EmptyToNull(GetConfigValue("api.ensure-importable-video"));
        return (configValue != null ? bool.Parse(configValue) : defaultValue);
    }

    public bool ShowHiddenWebdavFiles()
    {
        var defaultValue = false;
        var configValue = StringUtil.EmptyToNull(GetConfigValue("webdav.show-hidden-files"));
        return (configValue != null ? bool.Parse(configValue) : defaultValue);
    }

    public string? GetLibraryDir()
    {
        return StringUtil.EmptyToNull(GetConfigValue("media.library-dir"));
    }

    public int GetMaxQueueConnections()
    {
        return int.Parse(
            StringUtil.EmptyToNull(GetConfigValue("api.max-queue-connections"))
            ?? GetMaxConnections().ToString()
        );
    }

    public bool IsEnforceReadonlyWebdavEnabled()
    {
        var defaultValue = true;
        var configValue = StringUtil.EmptyToNull(GetConfigValue("webdav.enforce-readonly"));
        return (configValue != null ? bool.Parse(configValue) : defaultValue);
    }

    public bool IsIntegrityCheckingEnabled()
    {
        var configValue = StringUtil.EmptyToNull(GetConfigValue("integrity.enabled"));
        return configValue?.ToLowerInvariant() == "true";
    }

    public bool IsScheduledIntegrityCheckingEnabled()
    {
        var configValue = StringUtil.EmptyToNull(GetConfigValue("integrity.scheduled_enabled"));
        return configValue?.ToLowerInvariant() == "true";
    }

    public int GetIntegrityCheckIntervalHours()
    {
        if (int.TryParse(GetConfigValue("integrity.interval_hours"), out var hours))
            return Math.Max(1, hours);
        return 24; // Default to every 24 hours
    }

    public int GetIntegrityCheckRecheckEligibilityDays()
    {
        if (int.TryParse(GetConfigValue("integrity.recheck_eligibility_days"), out var days))
            return Math.Max(1, days);
        return 7; // Default to every 7 days
    }

    public int GetMaxFilesToCheckPerRun()
    {
        if (int.TryParse(GetConfigValue("integrity.max_files_per_run"), out var maxFiles))
            return Math.Max(1, maxFiles);
        return 100; // Default to 100 files per run
    }

    public string GetCorruptFileAction()
    {
        return GetConfigValue("integrity.corrupt_file_action") ?? "log";
    }

    public bool IsDirectDeletionFallbackEnabled()
    {
        if (bool.TryParse(GetConfigValue("integrity.direct_deletion_fallback"), out var enabled))
            return enabled;
        return false; // Default to disabled for safety
    }

    public bool IsMp4DeepScanEnabled()
    {
        if (bool.TryParse(GetConfigValue("integrity.mp4_deep_scan"), out var enabled))
            return enabled;
        return false; // Default to disabled for performance
    }

    public bool IsAutoMonitorEnabled()
    {
        if (bool.TryParse(GetConfigValue("integrity.auto_monitor"), out var enabled))
            return enabled;
        return false; // Default to disabled for safety
    }

    public bool IsUnmonitorValidatedFilesEnabled()
    {
        if (bool.TryParse(GetConfigValue("integrity.unmonitor_validated_files"), out var enabled))
            return enabled;
        return false; // Default to disabled for safety
    }

    public int GetNzbSegmentThresholdPercentage()
    {
        if (int.TryParse(GetConfigValue("integrity.nzb_segment_threshold_percentage"), out var threshold))
            return Math.Max(80, Math.Min(100, threshold)); // Clamp between 80-100
        return 92; // Default value
    }

    public int GetNzbSegmentSamplePercentage()
    {
        if (int.TryParse(GetConfigValue("integrity.nzb_segment_sample_percentage"), out var samplePct))
            return Math.Max(1, Math.Min(100, samplePct)); // Clamp between 1-100
        return 25; // Default value
    }

    public IntegrityCheckRunParameters GetDefaultRunParameters()
    {
        return new IntegrityCheckRunParameters
        {
            ScanDirectory = GetLibraryDir(),
            MaxFilesToCheck = GetMaxFilesToCheckPerRun(),
            CorruptFileAction = Enum.TryParse<IntegrityCheckRun.CorruptFileActionOption>(GetCorruptFileAction(), true, out var action) ? action : IntegrityCheckRun.CorruptFileActionOption.Log,
            Mp4DeepScan = IsMp4DeepScanEnabled(),
            AutoMonitor = IsAutoMonitorEnabled(),
            UnmonitorValidatedFiles = IsUnmonitorValidatedFilesEnabled(),
            DirectDeletionFallback = IsDirectDeletionFallbackEnabled(),
            NzbSegmentSamplePercentage = GetNzbSegmentSamplePercentage(),
            NzbSegmentThresholdPercentage = GetNzbSegmentThresholdPercentage(),
            RunType = IntegrityCheckRun.RunTypeOption.Manual
        };
    }

    public class ConfigEventArgs : EventArgs
    {
        public Dictionary<string, string> ChangedConfig { get; set; } = new();
        public Dictionary<string, string> NewConfig { get; set; } = new();
    }
}