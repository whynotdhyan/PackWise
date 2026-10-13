using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PackWise.Core.Duplicates;
using PackWise.Core.Files;
using PackWise.Services;

namespace PackWise.ViewModels;

public sealed record SizeOption(long Bytes, string Label);

/// <summary>Group header data shown above each set of identical files.</summary>
public sealed class DuplicateGroupHeader
{
    public DuplicateGroupHeader(int number, DuplicateGroup group)
    {
        Number = number;
        Group = group;
    }

    public int Number { get; }

    public DuplicateGroup Group { get; }

    public string Title => $"{Group.Files.Count} identical copies · {ByteSize.Format(Group.Size)} each";

    public string Detail =>
        $"{ByteSize.Format(Group.ReclaimableBytes)} reclaimable · " +
        (Group.Verification == DuplicateVerification.ByteForByte ? "verified byte for byte" : "SHA-256 match") +
        $" · {Group.Sha256[..12]}…";
}

public sealed partial class DuplicateRow : ObservableObject
{
    private readonly Action _onMarkedChanged;

    public DuplicateRow(DuplicateFile file, DuplicateGroupHeader group, string folder, Action onMarkedChanged)
    {
        File = file;
        Group = group;
        Folder = folder;
        _onMarkedChanged = onMarkedChanged;
    }

    public DuplicateFile File { get; }

    public DuplicateGroupHeader Group { get; }

    public string Name => File.Name;

    /// <summary>Folder shown relative to the scanned root, so the part that differs between copies stays visible.</summary>
    public string Folder { get; }

    public string FullFolder => File.Directory;

    public string SizeText => ByteSize.Format(File.Size);

    public string ModifiedText => File.ModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public FileKind Kind => FileKinds.FromName(File.Name);

    public bool IsHardLink => File.IsHardLinkOfAnother;

    [ObservableProperty]
    public partial bool IsMarked { get; set; }

    partial void OnIsMarkedChanged(bool value) => _onMarkedChanged();
}

public sealed partial class DuplicateFinderViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ObservableCollection<DuplicateRow> _rows = [];
    private CancellationTokenSource? _scanCancellation;
    private DuplicateScanResult? _result;
    private bool _suspendTotals;

    public DuplicateFinderViewModel(AppServices services, IEnumerable<string> roots)
    {
        _services = services;
        Folders = new ObservableCollection<string>(roots.Distinct(StringComparer.OrdinalIgnoreCase));
        Rows = new ListCollectionView(_rows);
        Rows.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DuplicateRow.Group)));
        Rows.Filter = o => FilterText.Length == 0 || ((DuplicateRow)o).Name.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase);
        MinimumSize = SizeOptions[1];
    }

    public ObservableCollection<string> Folders { get; }

    public ListCollectionView Rows { get; }

    public IReadOnlyList<SizeOption> SizeOptions { get; } =
    [
        new(1, "Any size"),
        new(1024, "1 KB or larger"),
        new(100 * 1024, "100 KB or larger"),
        new(1024 * 1024, "1 MB or larger"),
        new(10L * 1024 * 1024, "10 MB or larger"),
        new(100L * 1024 * 1024, "100 MB or larger"),
    ];

    [ObservableProperty]
    public partial SizeOption MinimumSize { get; set; }

    [ObservableProperty]
    public partial bool IncludeHidden { get; set; }

    [ObservableProperty]
    public partial bool IncludeSystemFolders { get; set; }

    [ObservableProperty]
    public partial bool SkipCloudFiles { get; set; } = true;

    [ObservableProperty]
    public partial bool VerifyBytes { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CancelScanCommand))]
    public partial bool IsScanning { get; private set; }

    [ObservableProperty]
    public partial string StageText { get; private set; } = "Choose folders, then start the scan.";

    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    [ObservableProperty]
    public partial bool ProgressIndeterminate { get; private set; }

    [ObservableProperty]
    public partial bool HasResults { get; private set; }

    [ObservableProperty]
    public partial string GroupCountText { get; private set; } = "—";

    [ObservableProperty]
    public partial string FileCountText { get; private set; } = "—";

    [ObservableProperty]
    public partial string ReclaimableText { get; private set; } = "—";

    [ObservableProperty]
    public partial string? ScanNote { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecycleCommand), nameof(MoveToReviewCommand))]
    public partial int MarkedCount { get; private set; }

    [ObservableProperty]
    public partial string MarkedText { get; private set; } = "No files selected";

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ResultMessage { get; private set; }

    public event EventHandler? AddFolderRequested;

    partial void OnFilterTextChanged(string value) => Rows.Refresh();

    public void AddFolders(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(Directory.Exists))
        {
            if (!Folders.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Folders.Add(path);
            }
        }

        ScanCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddFolder() => AddFolderRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RemoveFolder(string path)
    {
        Folders.Remove(path);
        ScanCommand.NotifyCanExecuteChanged();
    }

    private bool CanScan() => !IsScanning && Folders.Count > 0;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task Scan()
    {
        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        ResultMessage = null;
        ProgressIndeterminate = true;
        _rows.Clear();
        HasResults = false;
        UpdateMarked();

        var progress = new Progress<DuplicateScanProgress>(ReportProgress);
        try
        {
            _result = await DuplicateScanner.ScanAsync(new DuplicateScanOptions
            {
                Roots = Folders.ToList(),
                MinimumSize = MinimumSize.Bytes,
                IncludeHiddenFiles = IncludeHidden,
                IncludeSystemFolders = IncludeSystemFolders,
                SkipCloudPlaceholders = SkipCloudFiles,
                VerifyByteByByte = VerifyBytes,
            }, progress, _scanCancellation.Token);

            ShowResult(_result);
            _services.Log.Info("duplicates", $"Scanned {_result.FilesScanned} files; {_result.Groups.Count} duplicate groups; {_result.ReclaimableBytes} bytes reclaimable.", string.Join("; ", Folders));
        }
        finally
        {
            IsScanning = false;
            ProgressIndeterminate = false;
            _scanCancellation.Dispose();
            _scanCancellation = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void CancelScan() => _scanCancellation?.Cancel();

    private void ReportProgress(DuplicateScanProgress p)
    {
        StageText = p.Stage switch
        {
            DuplicateScanStage.Enumerating => $"Looking for files… {p.FilesFound:N0} found",
            DuplicateScanStage.Sampling => $"Comparing {p.CandidateFiles:N0} files of matching size…",
            DuplicateScanStage.Hashing => "Reading contents of likely duplicates…",
            DuplicateScanStage.Verifying => "Comparing byte by byte…",
            _ => "Finishing…",
        };

        if (p.BytesToRead > 0 && p.Stage is DuplicateScanStage.Sampling or DuplicateScanStage.Hashing)
        {
            ProgressIndeterminate = false;
            ProgressValue = Math.Clamp(100.0 * p.BytesRead / p.BytesToRead, 0, 100);
        }
        else
        {
            ProgressIndeterminate = true;
        }
    }

    private void ShowResult(DuplicateScanResult result)
    {
        _suspendTotals = true;
        var number = 0;
        foreach (var group in result.Groups)
        {
            var header = new DuplicateGroupHeader(++number, group);
            foreach (var file in group.Files)
            {
                _rows.Add(new DuplicateRow(file, header, DisplayFolder(file.Directory), UpdateMarked));
            }
        }

        _suspendTotals = false;
        HasResults = result.Groups.Count > 0;
        GroupCountText = result.Groups.Count.ToString("N0", CultureInfo.CurrentCulture);
        FileCountText = result.Groups.Sum(g => g.Files.Count).ToString("N0", CultureInfo.CurrentCulture);
        ReclaimableText = ByteSize.Format(result.ReclaimableBytes);

        var seconds = result.Duration.TotalSeconds;
        var notes = new List<string> { $"{result.FilesScanned:N0} files checked in " + (seconds < 1 ? "under a second" : $"{seconds:0.#} s") };
        if (result.SkippedCloudFiles > 0)
        {
            notes.Add($"{result.SkippedCloudFiles:N0} online-only cloud files skipped (not downloaded)");
        }

        if (result.UnreadableFiles > 0)
        {
            notes.Add($"{result.UnreadableFiles:N0} files couldn't be read");
        }

        ScanNote = string.Join(" · ", notes);
        StageText = result.Canceled ? "Scan canceled." : result.Groups.Count == 0 ? "No duplicates found." : "Review the groups below. Nothing is removed until you choose an action.";
        ProgressValue = result.Canceled ? 0 : 100;
        UpdateMarked();
    }

    private string DisplayFolder(string directory)
    {
        var root = Folders
            .Where(r => Core.Platform.FileSystemInfoEx.IsWithin(r, directory) || directory.Equals(Path.TrimEndingDirectorySeparator(r), StringComparison.OrdinalIgnoreCase))
            .MaxBy(r => r.Length);
        if (root is null)
        {
            return directory;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        var relative = Path.GetRelativePath(root, directory);
        return relative == "." ? name : Path.Combine(name, relative);
    }

    [RelayCommand]
    private void SelectKeeping(string rule)
    {
        if (_result is null)
        {
            return;
        }

        var keepRule = Enum.Parse<KeepRule>(rule);
        string? preferred = null;
        if (keepRule == KeepRule.InPreferredFolder)
        {
            preferred = Folders.FirstOrDefault();
        }

        var marked = KeepRules.SelectRemovals(_result.Groups, keepRule, preferred);
        _suspendTotals = true;
        foreach (var row in _rows)
        {
            row.IsMarked = marked.Contains(row.File.Path);
        }

        _suspendTotals = false;
        UpdateMarked();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _suspendTotals = true;
        foreach (var row in _rows)
        {
            row.IsMarked = false;
        }

        _suspendTotals = false;
        UpdateMarked();
    }

    private bool HasMarked() => MarkedCount > 0;

    [RelayCommand(CanExecute = nameof(HasMarked))]
    private async Task Recycle()
    {
        var files = _rows.Where(r => r.IsMarked).Select(r => r.File).ToList();
        var outcome = await Task.Run(() => DuplicateActions.Recycle(files, _result!.Groups.ToList()));
        Complete(outcome, "moved to the Recycle Bin");
    }

    public async Task MoveToReviewAsync(string folder)
    {
        var files = _rows.Where(r => r.IsMarked).Select(r => r.File).ToList();
        var outcome = await Task.Run(() => DuplicateActions.MoveToReviewFolder(files, _result!.Groups.ToList(), folder));
        Complete(outcome, $"moved to {folder}");
    }

    public event EventHandler? MoveToReviewRequested;

    [RelayCommand(CanExecute = nameof(HasMarked))]
    private void MoveToReview() => MoveToReviewRequested?.Invoke(this, EventArgs.Empty);

    public string MarkedSummary => $"{MarkedCount:N0} files · {ByteSize.Format(MarkedBytes)}";

    private long MarkedBytes => _rows.Where(r => r.IsMarked && !r.IsHardLink).Sum(r => r.File.Size);

    private void Complete(DuplicateActionResult outcome, string verb)
    {
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = outcome.Failures.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows.Where(r => r.IsMarked && !failed.Contains(r.File.Path)).ToList())
        {
            done.Add(row.File.Path);
            _rows.Remove(row);
        }

        // Groups with a single remaining file are no longer duplicates.
        foreach (var lonely in _rows.GroupBy(r => r.Group).Where(g => g.Count() < 2).SelectMany(g => g).ToList())
        {
            _rows.Remove(lonely);
        }

        _services.Log.Info("duplicates", $"{outcome.Succeeded} file(s) {verb}; {outcome.Failures.Count} failed.");
        ResultMessage = outcome.Failures.Count == 0
            ? $"{outcome.Succeeded:N0} file(s) {verb}."
            : $"{outcome.Succeeded:N0} file(s) {verb}. {outcome.Failures.Count:N0} skipped: {outcome.Failures[0].Reason}";
        HasResults = _rows.Count > 0;
        UpdateMarked();
    }

    private void UpdateMarked()
    {
        if (_suspendTotals)
        {
            return;
        }

        MarkedCount = _rows.Count(r => r.IsMarked);
        MarkedText = MarkedCount == 0 ? "No files selected" : $"{MarkedSummary} selected";
    }
}
