using System.Text.Json;
using System.Text.Json.Serialization;

namespace PackWise.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> atomically, recovering from damaged files.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    /// <summary>Set when the stored file could not be read and defaults were used instead.</summary>
    public string? RecoveredFromBackup { get; private set; }

    public AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            var backup = $"{_path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(_path, backup, overwrite: true);
                RecoveredFromBackup = backup;
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
            }

            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, settings, JsonOptions);
        }

        File.Move(temporary, _path, overwrite: true);
    }

    public void Reset()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
