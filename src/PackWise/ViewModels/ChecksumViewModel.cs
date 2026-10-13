using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PackWise.Core.Files;
using PackWise.Core.Hashing;

namespace PackWise.ViewModels;

public sealed partial class ChecksumRow : ObservableObject
{
    public ChecksumRow(string path) => Path = path;

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    public FileKind Kind => FileKinds.FromName(Path);

    [ObservableProperty]
    public partial string SizeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Crc32 { get; set; }

    [ObservableProperty]
    public partial string? Sha256 { get; set; }

    [ObservableProperty]
    public partial string? Sha512 { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Waiting";

    [ObservableProperty]
    public partial bool IsMatch { get; set; }

    public FileChecksums? Result { get; set; }
}

public sealed partial class VerifyRow : ObservableObject
{
    public required string Name { get; init; }

    public required string Expected { get; init; }

    public string? Actual { get; init; }

    public required ChecksumMatch Result { get; init; }

    public string ResultText => Result switch
    {
        ChecksumMatch.Match => "OK",
        ChecksumMatch.Mismatch => "Different — file changed or damaged",
        ChecksumMatch.Missing => "File not found",
        _ => "Couldn't read the file",
    };

    public bool IsOk => Result == ChecksumMatch.Match;
}

public sealed partial class ChecksumViewModel : ObservableObject
{
    private CancellationTokenSource? _cancellation;

    public ChecksumViewModel(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            Rows.Add(new ChecksumRow(file));
        }
    }

    public ObservableCollection<ChecksumRow> Rows { get; } = [];

    public ObservableCollection<VerifyRow> VerifyRows { get; } = [];

    [ObservableProperty]
    public partial bool UseCrc32 { get; set; } = true;

    [ObservableProperty]
    public partial bool UseSha256 { get; set; } = true;

    [ObservableProperty]
    public partial bool UseSha512 { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ComputeCommand), nameof(CancelCommand), nameof(VerifyFileCommand))]
    public partial bool IsWorking { get; private set; }

    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Add files, choose algorithms, then compute.";

    [ObservableProperty]
    public partial string CompareText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? CompareResult { get; private set; }

    [ObservableProperty]
    public partial bool? CompareMatched { get; private set; }

    [ObservableProperty]
    public partial string? VerifySummary { get; private set; }

    public bool HasResults => Rows.Any(r => r.Result is not null);

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists))
        {
            if (!Rows.Any(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                Rows.Add(new ChecksumRow(path));
            }
        }

        ComputeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Clear()
    {
        Rows.Clear();
        CompareResult = null;
        CompareMatched = null;
        StatusText = "Add files, choose algorithms, then compute.";
        ComputeCommand.NotifyCanExecuteChanged();
    }

    private bool CanCompute() => !IsWorking && Rows.Count > 0 && (UseCrc32 || UseSha256 || UseSha512);

    partial void OnUseCrc32Changed(bool value) => ComputeCommand.NotifyCanExecuteChanged();

    partial void OnUseSha256Changed(bool value) => ComputeCommand.NotifyCanExecuteChanged();

    partial void OnUseSha512Changed(bool value) => ComputeCommand.NotifyCanExecuteChanged();

    partial void OnCompareTextChanged(string value) => UpdateComparison();

    [RelayCommand(CanExecute = nameof(CanCompute))]
    private async Task Compute()
    {
        var algorithms = (UseCrc32 ? ChecksumAlgorithms.Crc32 : 0) | (UseSha256 ? ChecksumAlgorithms.Sha256 : 0) | (UseSha512 ? ChecksumAlgorithms.Sha512 : 0);
        _cancellation = new CancellationTokenSource();
        IsWorking = true;
        var rows = Rows.ToList();
        var totalBytes = rows.Sum(r => File.Exists(r.Path) ? new FileInfo(r.Path).Length : 0);
        long completed = 0;

        try
        {
            foreach (var row in rows)
            {
                row.Status = "Reading…";
                StatusText = $"Computing {row.Name}…";
                var start = completed;
                var progress = new Progress<long>(done => ProgressValue = totalBytes == 0 ? 0 : 100.0 * (start + done) / totalBytes);
                try
                {
                    var result = await Task.Run(() => Checksums.Compute(row.Path, algorithms, progress, _cancellation.Token));
                    row.Result = result;
                    row.SizeText = ByteSize.Format(result.Size);
                    row.Crc32 = result.Crc32;
                    row.Sha256 = result.Sha256;
                    row.Sha512 = result.Sha512;
                    row.Status = "Done";
                    completed += result.Size;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    row.Status = ex.Message;
                }
            }

            StatusText = $"Computed checksums for {rows.Count} file(s).";
            ProgressValue = 100;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Stopped.";
        }
        finally
        {
            IsWorking = false;
            _cancellation.Dispose();
            _cancellation = null;
            OnPropertyChanged(nameof(HasResults));
            SaveCommand.NotifyCanExecuteChanged();
            UpdateComparison();
        }
    }

    [RelayCommand(CanExecute = nameof(IsWorking))]
    private void Cancel() => _cancellation?.Cancel();

    private bool CanSave() => HasResults;

    public event EventHandler? SaveRequested;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => SaveRequested?.Invoke(this, EventArgs.Empty);

    public string BuildChecksumFile(string targetPath)
    {
        var algorithm = UseSha256 ? ChecksumAlgorithms.Sha256 : UseSha512 ? ChecksumAlgorithms.Sha512 : ChecksumAlgorithms.Crc32;
        if (targetPath.EndsWith(".sha512", StringComparison.OrdinalIgnoreCase))
        {
            algorithm = ChecksumAlgorithms.Sha512;
        }

        return Checksums.ToChecksumFile(Rows.Where(r => r.Result is not null).Select(r => r.Result!), algorithm, Path.GetDirectoryName(targetPath)!);
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyFile(string path)
    {
        IsWorking = true;
        VerifyRows.Clear();
        VerifySummary = $"Checking files listed in {Path.GetFileName(path)}…";
        try
        {
            var results = await Task.Run(() => Checksums.Verify(path));
            foreach (var result in results)
            {
                VerifyRows.Add(new VerifyRow { Name = result.FileName, Expected = result.ExpectedHash, Actual = result.ActualHash, Result = result.Result });
            }

            var failed = results.Count(r => r.Result != ChecksumMatch.Match);
            VerifySummary = results.Count == 0
                ? "No checksums found in that file."
                : failed == 0 ? $"All {results.Count} file(s) match." : $"{failed} of {results.Count} file(s) don't match.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            VerifySummary = ex.Message;
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanVerify(string? path) => !IsWorking;

    private void UpdateComparison()
    {
        var expected = CompareText.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        foreach (var row in Rows)
        {
            row.IsMatch = false;
        }

        if (expected.Length == 0)
        {
            CompareResult = null;
            CompareMatched = null;
            return;
        }

        foreach (var row in Rows)
        {
            foreach (var (name, value) in new[] { ("CRC32", row.Crc32), ("SHA-256", row.Sha256), ("SHA-512", row.Sha512) })
            {
                if (value is not null && value.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    row.IsMatch = true;
                    CompareResult = $"Match: {row.Name} ({name})";
                    CompareMatched = true;
                    return;
                }
            }
        }

        CompareResult = HasResults ? "No file matches this checksum." : "Compute checksums first, then paste the expected value.";
        CompareMatched = HasResults ? false : null;
    }
}
