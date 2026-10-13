using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using PackWise.Archiving;
using PackWise.Core.Extraction;
using PackWise.Core.Files;
using PackWise.Core.Settings;

namespace PackWise.ViewModels;

public sealed record LevelOption(CompressionPreset Level, string Label, string Description);

public sealed partial class AddArchiveViewModel : ObservableObject
{
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#$%&*+-=?@_";

    private string _password = string.Empty;
    private string _confirmation = string.Empty;

    public AddArchiveViewModel(IReadOnlyList<string> sources, string folder, AppSettings settings, string? existingArchive = null)
    {
        Sources = sources;
        IsAddingToExisting = existingArchive is not null;
        FormatName = settings.DefaultFormat is "7z" ? "7z" : "zip";
        Level = settings.DefaultLevel;
        TestAfter = settings.TestAfterCreate;
        ArchivePath = existingArchive ?? Path.Combine(folder, SuggestName(sources) + "." + FormatName);
        _ = MeasureAsync();
    }

    public IReadOnlyList<string> Sources { get; }

    public bool IsAddingToExisting { get; }

    public static IReadOnlyList<LevelOption> AllLevels { get; } =
    [
        new(CompressionPreset.Store, "Store", "No compression. Fastest; good for photos, videos and other archives."),
        new(CompressionPreset.Fastest, "Fastest", "Light compression with minimal waiting."),
        new(CompressionPreset.Fast, "Fast", "Quick, with a reasonable size reduction."),
        new(CompressionPreset.Balanced, "Balanced", "A good trade-off for everyday use."),
        new(CompressionPreset.Maximum, "Maximum", "Smaller files; noticeably slower."),
        new(CompressionPreset.Smallest, "Smallest", "Best ratio; slowest and uses the most memory."),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsZip), nameof(Is7z), nameof(FormatNote))]
    public partial string FormatName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorText), nameof(CanCreate))]
    public partial string ArchivePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LevelDescription))]
    public partial CompressionPreset Level { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorText), nameof(CanCreate), nameof(EncryptionNote))]
    public partial bool UsePassword { get; set; }

    [ObservableProperty]
    public partial bool EncryptFileNames { get; set; } = true;

    [ObservableProperty]
    public partial bool TestAfter { get; set; }

    [ObservableProperty]
    public partial string SourceSummary { get; set; } = "Counting files…";

    [ObservableProperty]
    public partial int Strength { get; set; }

    [ObservableProperty]
    public partial string StrengthText { get; set; } = string.Empty;

    public bool IsZip
    {
        get => FormatName == "zip";
        set
        {
            if (value)
            {
                FormatName = "zip";
            }
        }
    }

    public bool Is7z
    {
        get => FormatName == "7z";
        set
        {
            if (value)
            {
                FormatName = "7z";
            }
        }
    }

    public IReadOnlyList<LevelOption> Levels => AllLevels;

    public string LevelDescription => Levels.FirstOrDefault(l => l.Level == Level)?.Description ?? string.Empty;

    public string FormatNote => IsZip
        ? "Opens almost anywhere, including Windows File Explorer and macOS."
        : "Usually smaller. Needs 7-Zip, PackWise or a compatible tool to open.";

    public string EncryptionNote => IsZip
        ? "AES-256. Windows File Explorer can't open encrypted ZIP files; recipients need PackWise, 7-Zip or similar."
        : "AES-256. Encrypting file names hides the list of files until the password is entered.";

    public string Password => _password;

    public string? ErrorText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ArchivePath))
            {
                return "Choose a name for the archive.";
            }

            var name = Path.GetFileName(ArchivePath);
            if (name.Length == 0 || name != EntryPathSanitizer.SanitizeFileName(name))
            {
                return "The archive name contains characters Windows doesn't allow.";
            }

            if (!Path.IsPathFullyQualified(ArchivePath))
            {
                return "Enter a full path, for example C:\\Users\\you\\Documents\\backup.zip.";
            }

            if (UsePassword && _password.Length == 0)
            {
                return "Enter a password, or turn off encryption.";
            }

            if (UsePassword && _password != _confirmation)
            {
                return "The passwords don't match.";
            }

            return null;
        }
    }

    public bool CanCreate => ErrorText is null;

    public void SetPassword(string password, string confirmation)
    {
        _password = password;
        _confirmation = confirmation;
        (Strength, StrengthText) = Estimate(password);
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(CanCreate));
    }

    public static string GeneratePassword(int length = 20)
    {
        return string.Create(length, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
            }
        });
    }

    public CreateArchiveOptions BuildOptions() => new()
    {
        Format = ArchiveWriter.FindFormat(FormatName),
        Level = Level,
        Password = UsePassword ? _password : null,
        EncryptFileNames = UsePassword && Is7z && EncryptFileNames,
    };

    partial void OnFormatNameChanged(string value)
    {
        if (!IsAddingToExisting && !string.IsNullOrWhiteSpace(ArchivePath))
        {
            ArchivePath = Path.ChangeExtension(ArchivePath, value);
        }

        OnPropertyChanged(nameof(EncryptionNote));
    }

    private static string SuggestName(IReadOnlyList<string> sources)
    {
        if (sources.Count == 1)
        {
            var path = sources[0].TrimEnd('\\');
            return Directory.Exists(path) ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        }

        var parent = Path.GetDirectoryName(sources[0].TrimEnd('\\'));
        var name = parent is null ? null : Path.GetFileName(parent);
        return string.IsNullOrEmpty(name) ? "Archive" : name;
    }

    private async Task MeasureAsync()
    {
        var (files, folders, bytes) = await Task.Run(() =>
        {
            long fileCount = 0, folderCount = 0, total = 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var source in Sources)
            {
                if (Directory.Exists(source))
                {
                    folderCount++;
                    foreach (var info in new DirectoryInfo(source).EnumerateFileSystemInfos("*", options))
                    {
                        if (info is FileInfo file)
                        {
                            fileCount++;
                            total += file.Length;
                        }
                        else
                        {
                            folderCount++;
                        }
                    }
                }
                else if (File.Exists(source))
                {
                    fileCount++;
                    total += new FileInfo(source).Length;
                }
            }

            return (fileCount, folderCount, total);
        }).ConfigureAwait(true);

        var parts = new List<string> { files == 1 ? "1 file" : $"{files:N0} files" };
        if (folders > 0)
        {
            parts.Add(folders == 1 ? "1 folder" : $"{folders:N0} folders");
        }

        SourceSummary = $"{string.Join(" in ", parts)} · {ByteSize.Format(bytes)} before compression";
    }

    private static (int Score, string Label) Estimate(string password)
    {
        if (password.Length == 0)
        {
            return (0, string.Empty);
        }

        var classes = 0;
        classes += password.Any(char.IsLower) ? 1 : 0;
        classes += password.Any(char.IsUpper) ? 1 : 0;
        classes += password.Any(char.IsDigit) ? 1 : 0;
        classes += password.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0;

        var score = password.Length switch
        {
            < 8 => 1,
            < 12 => classes >= 3 ? 2 : 1,
            < 16 => classes >= 3 ? 3 : 2,
            _ => classes >= 2 ? 4 : 3,
        };

        return (score, score switch
        {
            1 => "Weak — easy to guess",
            2 => "Fair",
            3 => "Good",
            _ => "Strong",
        });
    }
}
