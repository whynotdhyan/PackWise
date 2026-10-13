namespace PackWise.Core.Platform;

/// <summary>Where PackWise keeps its local state. Nothing is written elsewhere except user-chosen outputs.</summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Northstar Labs", "PackWise");
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string LogDirectory => Path.Combine(Root, "logs");

    public string TempRoot => Path.Combine(Root, "temp");
}

/// <summary>
/// Private scratch space for previews and nested archives. Each session gets its own folder,
/// removed on dispose; folders left by a crash are cleaned up on the next start.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    private readonly string _sessionRoot;

    public TempWorkspace(string tempRoot)
    {
        CleanStale(tempRoot);
        _sessionRoot = Path.Combine(tempRoot, $"{Environment.ProcessId}-{Guid.NewGuid():N}"[..20]);
        Directory.CreateDirectory(_sessionRoot);
    }

    public string SessionRoot => _sessionRoot;

    /// <summary>Creates a new, empty folder for one preview or nested archive.</summary>
    public string CreateFolder()
    {
        var folder = Path.Combine(_sessionRoot, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public void Dispose() => TryDeleteDirectory(_sessionRoot);

    private static void CleanStale(string tempRoot)
    {
        if (!Directory.Exists(tempRoot))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(tempRoot))
        {
            var name = Path.GetFileName(folder);
            var dash = name.IndexOf('-');
            if (dash > 0 && int.TryParse(name.AsSpan(0, dash), out var processId) && IsRunning(processId))
            {
                continue;
            }

            TryDeleteDirectory(folder);
        }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited && process.ProcessName.Contains("PackWise", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
