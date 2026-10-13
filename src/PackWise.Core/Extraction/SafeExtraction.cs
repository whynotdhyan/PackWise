using System.Diagnostics;
using PackWise.Archiving;
using PackWise.Core.Platform;
using PackWise.Core.Security;

namespace PackWise.Core.Extraction;

public sealed class ExtractionOptions
{
    public MarkOfTheWebPolicy MarkOfTheWeb { get; init; } = MarkOfTheWebPolicy.AllFiles;

    /// <summary>Zone.Identifier content read from the source archive; null when it carries no internet mark.</summary>
    public string? ZoneIdentifier { get; init; }

    /// <summary>Keep files whose checksum failed instead of deleting them (they are flagged in the report).</summary>
    public bool KeepDamagedFiles { get; init; }

    /// <summary>Send files being replaced to the Recycle Bin instead of overwriting them in place.</summary>
    public bool RecycleReplacedFiles { get; init; } = true;

    /// <summary>Maximum bytes the whole run may write. Defaults to free space minus a safety reserve.</summary>
    public long? ByteBudget { get; init; }

    public int MaxPasswordRetries { get; init; } = 3;
}

public enum ProblemKind
{
    Blocked,
    Skipped,
    Failed,
    Damaged,
}

public sealed record EntryProblem(string ArchivePath, string Message, ProblemKind Kind);

public sealed class ExtractionReport
{
    public required string Destination { get; init; }

    public int Written { get; init; }

    public int Skipped { get; init; }

    public int Blocked { get; init; }

    public int Failed { get; init; }

    public int Damaged { get; init; }

    public bool Canceled { get; init; }

    public bool PasswordCanceled { get; init; }

    public bool LimitExceeded { get; init; }

    public required IReadOnlyList<EntryProblem> Problems { get; init; }

    public required IReadOnlyList<string> WrittenFiles { get; init; }

    public TimeSpan Duration { get; init; }

    public bool Succeeded => Failed == 0 && !Canceled && !PasswordCanceled && !LimitExceeded;
}

/// <summary>Executes an <see cref="ExtractionPlan"/> through the engine with atomic, contained writes.</summary>
public static class SafeExtraction
{
    private const long FreeSpaceReserve = 256L * 1024 * 1024;

    public static ExtractionReport Execute(
        ArchiveReader reader,
        ExtractionPlan plan,
        ExtractionOptions options,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var problems = new List<EntryProblem>();
        Directory.CreateDirectory(plan.Destination);

        foreach (var item in plan.Items.Where(i => i.Action == PlanAction.CreateDirectory))
        {
            Directory.CreateDirectory(item.TargetPath!);
        }

        foreach (var item in plan.Items.Where(i => i.Action == PlanAction.Skip))
        {
            problems.Add(new EntryProblem(item.Entry.Path, item.DescribeSkip(), item.IsBlocked ? ProblemKind.Blocked : ProblemKind.Skipped));
        }

        var budget = options.ByteBudget ?? DefaultBudget(plan.Destination);
        var target = new PlanTarget(plan, options, budget);
        var pending = plan.Writes.Select(i => i.Entry.Index).ToList();
        ArchiveRunResult? run = null;
        var attempts = 0;

        while (pending.Count > 0)
        {
            run = reader.Extract(pending, target, progress, cancellationToken);
            pending = run.Outcomes
                .Where(o => o.Entry.IsEncrypted && o.Result is EntryResult.WrongPassword or EntryResult.DataError or EntryResult.CrcError)
                .Select(o => o.Entry.Index)
                .ToList();

            if (pending.Count == 0 || run.Canceled || run.PasswordCanceled || run.LimitExceeded || ++attempts > options.MaxPasswordRetries)
            {
                break;
            }

            target.ForgetFailures(pending);
        }

        target.DeleteLeftovers();
        ApplyDirectoryTimes(plan);

        if (run?.Error is { } error && !run.Canceled)
        {
            problems.Add(new EntryProblem(string.Empty, error.Message, ProblemKind.Failed));
        }

        problems.AddRange(target.Problems);

        return new ExtractionReport
        {
            Destination = plan.Destination,
            Written = target.WrittenFiles.Count,
            Skipped = plan.Items.Count(i => i.SkipReason == SkipReason.ExistingKept),
            Blocked = plan.BlockedCount,
            Failed = target.FailedCount,
            Damaged = target.DamagedCount,
            Canceled = run?.Canceled ?? false,
            PasswordCanceled = run?.PasswordCanceled ?? false,
            LimitExceeded = run?.LimitExceeded ?? false,
            Problems = problems,
            WrittenFiles = target.WrittenFiles,
            Duration = stopwatch.Elapsed,
        };
    }

    private static long DefaultBudget(string destination) =>
        FileSystemInfoEx.GetFreeSpace(destination) is { } free ? Math.Max(0, (long)free - FreeSpaceReserve) : long.MaxValue;

    private static void ApplyDirectoryTimes(ExtractionPlan plan)
    {
        foreach (var item in plan.Items.Where(i => i.Action == PlanAction.CreateDirectory && i.Entry.Modified is not null)
                                       .OrderByDescending(i => i.TargetPath!.Length))
        {
            try
            {
                Directory.SetLastWriteTimeUtc(item.TargetPath!, item.Entry.Modified!.Value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
    }

    private sealed class PlanTarget : IExtractTarget
    {
        private const FileAttributes RestorableAttributes = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive;

        private readonly Dictionary<int, PlannedEntry> _byIndex;
        private readonly ExtractionOptions _options;
        private readonly long _budget;
        private readonly Dictionary<int, string> _temporaryFiles = [];
        private readonly Dictionary<int, EntryProblem> _failures = [];
        private readonly List<string> _written = [];
        private long _bytesWritten;

        public PlanTarget(ExtractionPlan plan, ExtractionOptions options, long budget)
        {
            _byIndex = plan.Writes.ToDictionary(i => i.Entry.Index);
            _options = options;
            _budget = budget;
        }

        public IReadOnlyList<string> WrittenFiles => _written;

        public IEnumerable<EntryProblem> Problems => _failures.Values;

        public int FailedCount => _failures.Values.Count(p => p.Kind == ProblemKind.Failed);

        public int DamagedCount => _failures.Values.Count(p => p.Kind == ProblemKind.Damaged);

        public Stream? OpenEntry(ArchiveEntry entry)
        {
            if (!_byIndex.TryGetValue(entry.Index, out var item) || item.TargetPath is null)
            {
                return null;
            }

            var directory = Path.GetDirectoryName(item.TargetPath)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.pwpart");
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
            _temporaryFiles[entry.Index] = temporary;
            return stream;
        }

        public long GetByteLimit(ArchiveEntry entry) => Math.Max(0, _budget - _bytesWritten);

        public void CompleteEntry(ArchiveEntry entry, Stream stream, EntryResult result, long bytesWritten)
        {
            stream.Dispose();
            _bytesWritten += bytesWritten;

            if (!_temporaryFiles.Remove(entry.Index, out var temporary) || !_byIndex.TryGetValue(entry.Index, out var item))
            {
                return;
            }

            var keepDamaged = _options.KeepDamagedFiles && !entry.IsEncrypted && result is EntryResult.CrcError or EntryResult.DataError or EntryResult.UnexpectedEnd;
            if (result != EntryResult.Ok && !keepDamaged)
            {
                TryDelete(temporary);
                _failures[entry.Index] = new EntryProblem(entry.Path, result.Describe(), ProblemKind.Failed);
                return;
            }

            try
            {
                var finalPath = Commit(item, temporary);
                ApplyMetadata(entry, finalPath);
                _written.Add(finalPath);
                if (keepDamaged)
                {
                    _failures[entry.Index] = new EntryProblem(entry.Path, $"Kept damaged file: {result.Describe()}", ProblemKind.Damaged);
                }
                else
                {
                    _failures.Remove(entry.Index);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(temporary);
                _failures[entry.Index] = new EntryProblem(entry.Path, $"Could not write {item.TargetPath}: {ex.Message}", ProblemKind.Failed);
            }
        }

        public void ForgetFailures(IEnumerable<int> indices)
        {
            foreach (var index in indices)
            {
                _failures.Remove(index);
            }
        }

        public void DeleteLeftovers()
        {
            foreach (var temporary in _temporaryFiles.Values)
            {
                TryDelete(temporary);
            }

            _temporaryFiles.Clear();
        }

        private string Commit(PlannedEntry item, string temporary)
        {
            var target = item.TargetPath!;
            if (File.Exists(target))
            {
                if (item.ReplacesExisting)
                {
                    RemoveExisting(target);
                }
                else
                {
                    // Something appeared at the target after planning; never overwrite silently.
                    target = FileSystemInfoEx.MakeUnique(target);
                }
            }

            File.Move(temporary, target, overwrite: false);
            return target;
        }

        private void RemoveExisting(string target)
        {
            if (_options.RecycleReplacedFiles && RecycleBin.IsAvailableFor(target) && RecycleBin.Send([target]))
            {
                return;
            }

            var attributes = File.GetAttributes(target);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(target);
        }

        private void ApplyMetadata(ArchiveEntry entry, string path)
        {
            try
            {
                if (entry.Created is { } created)
                {
                    File.SetCreationTimeUtc(path, created);
                }

                if (entry.Modified is { } modified)
                {
                    File.SetLastWriteTimeUtc(path, modified);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }

            if (_options.ZoneIdentifier is { } zone && MarkOfTheWeb.ShouldApply(_options.MarkOfTheWeb, path))
            {
                MarkOfTheWeb.Write(path, zone);
            }

            if (entry.Attributes is { } raw && (raw & 0x8000) == 0)
            {
                var attributes = (FileAttributes)raw & RestorableAttributes;
                if (attributes != 0)
                {
                    File.SetAttributes(path, File.GetAttributes(path) | attributes);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
