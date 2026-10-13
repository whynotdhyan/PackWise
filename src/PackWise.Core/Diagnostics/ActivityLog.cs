using System.Globalization;
using System.Text.Json;

namespace PackWise.Core.Diagnostics;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Local, append-only JSON Lines log of operations (one file per day). Holds paths and outcomes
/// only; callers never pass passwords or file contents. Nothing is ever uploaded.
/// </summary>
public sealed class ActivityLog
{
    private readonly string _directory;
    private readonly Lock _sync = new();

    public ActivityLog(string directory, bool enabled = true)
    {
        _directory = directory;
        Enabled = enabled;
    }

    public bool Enabled { get; set; }

    public string Directory => _directory;

    public void Write(LogLevel level, string operation, string message, string? path = null, string? taskId = null)
    {
        if (!Enabled)
        {
            return;
        }

        var record = new Dictionary<string, string?>
        {
            ["time"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
            ["level"] = level.ToString().ToLowerInvariant(),
            ["operation"] = operation,
            ["task"] = taskId,
            ["path"] = path,
            ["message"] = message,
        };

        var line = JsonSerializer.Serialize(record) + Environment.NewLine;
        lock (_sync)
        {
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.AppendAllText(Path.Combine(_directory, $"packwise-{DateTime.Now:yyyy-MM-dd}.log"), line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never break an operation.
            }
        }
    }

    public void Info(string operation, string message, string? path = null, string? taskId = null) =>
        Write(LogLevel.Info, operation, message, path, taskId);

    public void Error(string operation, string message, string? path = null, string? taskId = null) =>
        Write(LogLevel.Error, operation, message, path, taskId);

    public void Prune(int retentionDays)
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return;
        }

        var cutoff = DateTime.Now.AddDays(-Math.Max(1, retentionDays));
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "packwise-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (!System.IO.Directory.Exists(_directory))
            {
                return;
            }

            foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "packwise-*.log"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
