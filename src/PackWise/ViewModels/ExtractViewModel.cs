using CommunityToolkit.Mvvm.ComponentModel;
using PackWise.Archiving;
using PackWise.Core.Extraction;
using PackWise.Core.Files;
using PackWise.Core.Platform;
using PackWise.Core.Security;
using PackWise.Core.Settings;

namespace PackWise.ViewModels;

/// <summary>What is about to be extracted, summarised before anything is written.</summary>
public sealed class ExtractionPreview
{
    public required string ArchiveName { get; init; }

    public int FileCount { get; init; }

    public ulong TotalBytes { get; init; }

    public bool NeedsPassword { get; init; }

    public int UnsafePaths { get; init; }

    public int AdjustedNames { get; init; }

    public int LinkEntries { get; init; }

    public IReadOnlyList<string> RiskyNames { get; init; } = [];

    public bool CameFromInternet { get; init; }

    /// <summary>True when everything sits in one top-level folder, so no extra subfolder is needed.</summary>
    public bool HasSingleRoot { get; init; }

    public static ExtractionPreview Build(string archiveName, IEnumerable<ArchiveEntry> entries, bool cameFromInternet, string baseFolder = "")
    {
        var files = 0;
        ulong bytes = 0;
        var encrypted = false;
        int unsafePaths = 0, adjusted = 0, links = 0;
        var risky = new List<string>();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefix = baseFolder.Length == 0 ? string.Empty : baseFolder.TrimEnd('\\') + "\\";

        foreach (var entry in entries)
        {
            var relative = entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? entry.Path[prefix.Length..] : entry.Path;
            var slash = relative.IndexOf('\\');
            roots.Add(slash < 0 ? relative : relative[..slash]);

            var sanitized = EntryPathSanitizer.Sanitize(relative);
            if (sanitized.IsRejected)
            {
                unsafePaths++;
                continue;
            }

            if (sanitized.Issues != PathIssues.None)
            {
                adjusted++;
            }

            if (entry.IsLink)
            {
                links++;
                continue;
            }

            if (entry.IsDirectory)
            {
                continue;
            }

            files++;
            bytes += entry.Size ?? 0;
            encrypted |= entry.IsEncrypted;
            var risk = FileRiskClassifier.Classify(entry.Name);
            if ((FileRiskClassifier.CanRunCode(risk) || risk.HasFlag(FileRisk.SpoofedName)) && risky.Count < 50)
            {
                risky.Add(entry.Name);
            }
        }

        return new ExtractionPreview
        {
            ArchiveName = archiveName,
            FileCount = files,
            TotalBytes = bytes,
            NeedsPassword = encrypted,
            UnsafePaths = unsafePaths,
            AdjustedNames = adjusted,
            LinkEntries = links,
            RiskyNames = risky,
            CameFromInternet = cameFromInternet,
            HasSingleRoot = roots.Count == 1,
        };
    }
}

public sealed record Notice(string Text, NoticeKind Kind, string? ActionText = null, Action? Action = null);

public enum NoticeKind
{
    Info,
    Success,
    Warning,
    Danger,
}

public sealed partial class ExtractViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public ExtractViewModel(ExtractionPreview preview, string destinationFolder, AppSettings settings, bool selectionOnly)
    {
        Preview = preview;
        _settings = settings;
        SelectionOnly = selectionOnly;
        RecentDestinations = settings.RecentDestinations.ToList();
        Destination = destinationFolder;
        SubfolderName = EntryPathSanitizer.SanitizeFileName(StripArchiveExtensions(preview.ArchiveName));
        CreateSubfolder = settings.ExtractToNewFolder && !preview.HasSingleRoot && preview.FileCount > 1;
        Policy = settings.ConflictPolicy;
        OpenWhenDone = settings.OpenFolderAfterExtract;
        Notices = BuildNotices(preview, settings);
    }

    public ExtractionPreview Preview { get; }

    public bool SelectionOnly { get; }

    public IReadOnlyList<string> RecentDestinations { get; }

    public IReadOnlyList<Notice> Notices { get; }

    public string Summary =>
        $"{(Preview.FileCount == 1 ? "1 file" : $"{Preview.FileCount:N0} files")} · {ByteSize.Format(Preview.TotalBytes)}" +
        (SelectionOnly ? " · selected items" : string.Empty);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinalDestination), nameof(SpaceText), nameof(IsSpaceLow))]
    public partial string Destination { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinalDestination))]
    public partial bool CreateSubfolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinalDestination))]
    public partial string SubfolderName { get; set; }

    [ObservableProperty]
    public partial ConflictPolicy Policy { get; set; }

    [ObservableProperty]
    public partial bool PreservePaths { get; set; } = true;

    [ObservableProperty]
    public partial bool OpenWhenDone { get; set; }

    public string FinalDestination
    {
        get
        {
            try
            {
                var root = Path.GetFullPath(Destination.Trim());
                return CreateSubfolder && !string.IsNullOrWhiteSpace(SubfolderName)
                    ? Path.Combine(root, EntryPathSanitizer.SanitizeFileName(SubfolderName.Trim()))
                    : root;
            }
            catch (ArgumentException)
            {
                return Destination;
            }
        }
    }

    public bool IsDestinationValid
    {
        get
        {
            try
            {
                return Path.IsPathFullyQualified(Destination.Trim());
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    public ulong? FreeSpace => IsDestinationValid ? FileSystemInfoEx.GetFreeSpace(Destination.Trim()) : null;

    public bool IsSpaceLow => FreeSpace is { } free && free < Preview.TotalBytes;

    public string SpaceText => FreeSpace is { } free
        ? $"{ByteSize.Format(free)} free on this drive" + (IsSpaceLow ? " — not enough space" : string.Empty)
        : "Free space unknown";

    public void RememberChoices()
    {
        _settings.ConflictPolicy = Policy;
        _settings.OpenFolderAfterExtract = OpenWhenDone;
        _settings.Remember(_settings.RecentDestinations, Destination.Trim());
    }

    private static List<Notice> BuildNotices(ExtractionPreview preview, AppSettings settings)
    {
        var notices = new List<Notice>();
        if (preview.UnsafePaths > 0)
        {
            notices.Add(new($"{preview.UnsafePaths} item(s) try to write outside the destination folder and will be blocked.", NoticeKind.Danger));
        }

        if (preview.LinkEntries > 0)
        {
            notices.Add(new($"{preview.LinkEntries} link(s) inside the archive will not be created.", NoticeKind.Warning));
        }

        if (preview.RiskyNames.Count > 0)
        {
            var examples = string.Join(", ", preview.RiskyNames.Take(3)) + (preview.RiskyNames.Count > 3 ? "…" : string.Empty);
            notices.Add(new($"Contains {preview.RiskyNames.Count} program, script or macro file(s): {examples}. Only open them if you trust the source.", NoticeKind.Warning));
        }

        if (preview.AdjustedNames > 0)
        {
            notices.Add(new($"{preview.AdjustedNames} name(s) will be adjusted so Windows can store them safely.", NoticeKind.Info));
        }

        if (preview.CameFromInternet && settings.MarkOfTheWeb != MarkOfTheWebPolicy.Off)
        {
            notices.Add(new("This archive came from the internet. Extracted files keep that mark, so Windows and Office can warn before opening them.", NoticeKind.Info));
        }

        if (preview.NeedsPassword)
        {
            notices.Add(new("Some files are encrypted. You'll be asked for the password.", NoticeKind.Info));
        }

        return notices;
    }

    private static string StripArchiveExtensions(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        foreach (var inner in new[] { ".tar", ".part1", ".part01", ".part001", ".7z", ".zip" })
        {
            if (stem.EndsWith(inner, StringComparison.OrdinalIgnoreCase))
            {
                stem = stem[..^inner.Length];
            }
        }

        return stem.Length == 0 ? "Extracted" : stem;
    }
}
