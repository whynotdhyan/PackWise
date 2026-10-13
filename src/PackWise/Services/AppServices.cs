using PackWise.Core.Diagnostics;
using PackWise.Core.Platform;
using PackWise.Core.Settings;

namespace PackWise.Services;

/// <summary>Process-wide services, created once at startup.</summary>
public sealed class AppServices : IDisposable
{
    private AppServices(AppPaths paths, SettingsStore store, AppSettings settings, ActivityLog log, TempWorkspace temp)
    {
        Paths = paths;
        SettingsStore = store;
        Settings = settings;
        Log = log;
        Temp = temp;
    }

    public static AppServices Current { get; private set; } = null!;

    public AppPaths Paths { get; }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; }

    public ActivityLog Log { get; }

    public TempWorkspace Temp { get; }

    /// <summary>Passwords typed this session, keyed by archive path. Memory only, never persisted.</summary>
    public PasswordCache Passwords { get; } = new();

    public static AppServices Initialize()
    {
        var paths = new AppPaths();
        var store = new SettingsStore(paths.SettingsFile);
        var settings = store.Load();
        var log = new ActivityLog(paths.LogDirectory, settings.LoggingEnabled);
        log.Prune(settings.LogRetentionDays);
        if (store.RecoveredFromBackup is { } backup)
        {
            log.Write(LogLevel.Warning, "settings", "Settings file was unreadable and has been reset.", backup);
        }

        Current = new AppServices(paths, store, settings, log, new TempWorkspace(paths.TempRoot));
        return Current;
    }

    public void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("settings", $"Could not save settings: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Passwords.Clear();
        Temp.Dispose();
    }
}

public sealed class PasswordCache
{
    private readonly Dictionary<string, string> _passwords = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();

    public string? Get(string archivePath)
    {
        lock (_sync)
        {
            return _passwords.GetValueOrDefault(archivePath);
        }
    }

    public void Set(string archivePath, string? password)
    {
        lock (_sync)
        {
            if (string.IsNullOrEmpty(password))
            {
                _passwords.Remove(archivePath);
            }
            else
            {
                _passwords[archivePath] = password;
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _passwords.Clear();
        }
    }
}

public static class AppLinks
{
    /// <summary>Public source repository.</summary>
    public const string Repository = "https://github.com/whynotdhyan/PackWise";

    public const string Issues = Repository + "/issues";

    public const string SecurityAdvisory = Repository + "/security/advisories/new";

    /// <summary>Buy Me a Coffee page. Leave empty to hide the support link.</summary>
    public const string Support = "https://buymeacoffee.com/whynotdhyan";
}
