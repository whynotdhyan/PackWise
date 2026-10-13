using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.IO.Hashing;
using System.Security.Cryptography;
using PackWise.Core.Platform;

namespace PackWise.Core.Duplicates;

/// <summary>
/// Finds files with identical content. Stages narrow the field cheaply before any full read:
/// size → sampled fingerprint (head, middle, tail) → full SHA-256 → optional byte comparison.
/// File names never count as evidence.
/// </summary>
public static class DuplicateScanner
{
    private const int SampleBlock = 16 * 1024;
    private const int ReadBuffer = 1024 * 1024;

    public static async Task<DuplicateScanResult> ScanAsync(
        DuplicateScanOptions options,
        IProgress<DuplicateScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var stopwatch = Stopwatch.StartNew();
        var state = new ScanState(progress);

        try
        {
            var files = await Task.Run(() => Enumerate(options, state, cancellationToken), cancellationToken).ConfigureAwait(false);

            var sizeGroups = files.GroupBy(f => f.Size).Where(g => g.Count() > 1).Select(g => g.ToList()).ToList();
            state.Candidates = sizeGroups.Sum(g => g.Count);

            var sampled = await SplitByAsync(sizeGroups, options, DuplicateScanStage.Sampling, state, SampleFingerprint, cancellationToken).ConfigureAwait(false);
            var hashed = await SplitByAsync(sampled.Select(g => g.Files).ToList(), options, DuplicateScanStage.Hashing, state, FullHash, cancellationToken).ConfigureAwait(false);

            var groups = new List<DuplicateGroup>();
            foreach (var (hash, members) in hashed)
            {
                var duplicate = BuildGroup(hash, members);
                if (duplicate is null)
                {
                    continue;
                }

                if (options.VerifyByteByByte)
                {
                    duplicate = await VerifyAsync(duplicate, state, cancellationToken).ConfigureAwait(false);
                }

                if (duplicate is not null)
                {
                    groups.Add(duplicate);
                }
            }

            state.Report(DuplicateScanStage.Done, null);
            return new DuplicateScanResult
            {
                Groups = groups.OrderByDescending(g => g.ReclaimableBytes).ToList(),
                FilesScanned = state.FilesFound,
                BytesHashed = state.BytesRead,
                SkippedCloudFiles = state.CloudSkipped,
                UnreadableFiles = state.Unreadable,
                Duration = stopwatch.Elapsed,
            };
        }
        catch (OperationCanceledException)
        {
            return new DuplicateScanResult
            {
                Groups = [],
                FilesScanned = state.FilesFound,
                BytesHashed = state.BytesRead,
                SkippedCloudFiles = state.CloudSkipped,
                UnreadableFiles = state.Unreadable,
                Canceled = true,
                Duration = stopwatch.Elapsed,
            };
        }
    }

    /// <summary>Folders skipped unless the user opts in: the OS, installed programs and system bookkeeping.</summary>
    public static IReadOnlyList<string> SystemFolders()
    {
        var folders = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            folders.Add(Path.Combine(drive.Name, "$Recycle.Bin"));
            folders.Add(Path.Combine(drive.Name, "System Volume Information"));
            folders.Add(Path.Combine(drive.Name, "$WinREAgent"));
        }

        return folders.Where(f => f.Length > 0).Select(f => Path.TrimEndingDirectorySeparator(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<DuplicateFile> Enumerate(DuplicateScanOptions options, ScanState state, CancellationToken cancellationToken)
    {
        var excluded = options.ExcludedFolders
            .Concat(options.IncludeSystemFolders ? [] : SystemFolders())
            .Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)))
            .ToList();

        var skip = FileAttributes.ReparsePoint | FileAttributes.Device;
        if (!options.IncludeHiddenFiles)
        {
            skip |= FileAttributes.Hidden | FileAttributes.System;
        }

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = skip,
            ReturnSpecialDirectories = false,
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<DuplicateFile>();

        foreach (var root in options.Roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var enumerable = new FileSystemEnumerable<DuplicateFile?>(root, Transform, enumerationOptions)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory,
                ShouldRecursePredicate = (ref FileSystemEntry entry) => !IsExcluded(entry.ToFullPath(), excluded),
            };

            foreach (var file in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file is null || !seen.Add(file.Path))
                {
                    continue;
                }

                files.Add(file);
                state.FilesFound++;
                if ((state.FilesFound & 0x3FF) == 0)
                {
                    state.Report(DuplicateScanStage.Enumerating, file.Path);
                }
            }
        }

        return files;

        DuplicateFile? Transform(ref FileSystemEntry entry)
        {
            if (entry.Length < Math.Max(1, options.MinimumSize))
            {
                return null;
            }

            if (options.SkipCloudPlaceholders && FileSystemInfoEx.IsCloudPlaceholder(entry.Attributes))
            {
                state.CloudSkipped++;
                return null;
            }

            return new DuplicateFile
            {
                Path = entry.ToFullPath(),
                Size = entry.Length,
                ModifiedUtc = entry.LastWriteTimeUtc.UtcDateTime,
                CreatedUtc = entry.CreationTimeUtc.UtcDateTime,
            };
        }
    }

    private static bool IsExcluded(string directory, List<string> excluded)
    {
        foreach (var folder in excluded)
        {
            if (directory.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                (directory.Length > folder.Length && directory[folder.Length] == '\\' && directory.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private delegate string? KeyFunction(DuplicateFile file, ScanState state, CancellationToken cancellationToken);

    private static async Task<List<(string Key, List<DuplicateFile> Files)>> SplitByAsync(
        List<List<DuplicateFile>> groups,
        DuplicateScanOptions options,
        DuplicateScanStage stage,
        ScanState state,
        KeyFunction key,
        CancellationToken cancellationToken)
    {
        state.BytesToRead = stage == DuplicateScanStage.Hashing
            ? groups.Sum(g => g.Sum(f => f.Size))
            : groups.Sum(g => g.Sum(f => Math.Min(f.Size, 3L * SampleBlock)));
        state.BytesRead = 0;
        state.Report(stage, null);

        var keys = new ConcurrentDictionary<DuplicateFile, string?>();
        var all = groups.SelectMany(g => g).ToList();
        await Parallel.ForEachAsync(
            all,
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism, CancellationToken = cancellationToken },
            (file, token) =>
            {
                keys[file] = key(file, state, token);
                state.Report(stage, file.Path);
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        var result = new List<(string Key, List<DuplicateFile> Files)>();
        foreach (var group in groups)
        {
            foreach (var bucket in group.Where(f => keys.GetValueOrDefault(f) is not null).GroupBy(f => keys[f]!))
            {
                var members = bucket.ToList();
                if (members.Count > 1)
                {
                    result.Add((bucket.Key, members));
                }
            }
        }

        return result;
    }

    private static string? SampleFingerprint(DuplicateFile file, ScanState state, CancellationToken cancellationToken)
    {
        // Small files are fully covered by the samples; hashing them again later is cheap.
        var buffer = ArrayPool<byte>.Shared.Rent(SampleBlock);
        try
        {
            using var stream = OpenForRead(file, state);
            if (stream is null)
            {
                return null;
            }

            var hash = new XxHash128();
            var offsets = file.Size <= 3L * SampleBlock
                ? [0L]
                : new[] { 0L, (file.Size / 2) - (SampleBlock / 2), file.Size - SampleBlock };

            foreach (var offset in offsets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Position = offset;
                var toRead = (int)Math.Min(file.Size <= 3L * SampleBlock ? file.Size : SampleBlock, int.MaxValue);
                var remaining = toRead;
                while (remaining > 0)
                {
                    var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                    if (read == 0)
                    {
                        return null;
                    }

                    hash.Append(buffer.AsSpan(0, read));
                    remaining -= read;
                    state.AddRead(read);
                }
            }

            return Convert.ToHexString(hash.GetCurrentHash());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state.MarkUnreadable();
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string? FullHash(DuplicateFile file, ScanState state, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBuffer);
        try
        {
            using var stream = OpenForRead(file, state);
            if (stream is null)
            {
                return null;
            }

            file.Identity = FileSystemInfoEx.GetIdentity(stream.SafeFileHandle);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read;
            long total = 0;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
                total += read;
                state.AddRead(read);
            }

            // A file that changed size while scanning cannot be trusted as a match.
            return total == file.Size ? Convert.ToHexString(sha.GetHashAndReset()) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state.MarkUnreadable();
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static DuplicateGroup? BuildGroup(string sha256, List<DuplicateFile> files)
    {
        var seenIdentities = new HashSet<FileIdentity>();
        foreach (var file in files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (file.Identity is { } identity)
            {
                var key = identity with { LinkCount = 0 };
                file.IsHardLinkOfAnother = !seenIdentities.Add(key);
            }
        }

        var group = new DuplicateGroup
        {
            Size = files[0].Size,
            Sha256 = sha256.ToLowerInvariant(),
            Files = files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            Verification = DuplicateVerification.ContentHash,
        };

        return group.PhysicalCopies > 1 ? group : null;
    }

    private static async Task<DuplicateGroup?> VerifyAsync(DuplicateGroup group, ScanState state, CancellationToken cancellationToken)
    {
        var reference = group.Files.First(f => !f.IsHardLinkOfAnother);
        var confirmed = new List<DuplicateFile> { reference };
        foreach (var candidate in group.Files.Where(f => f != reference))
        {
            if (candidate.IsHardLinkOfAnother || await Task.Run(() => ContentEquals(reference, candidate, state, cancellationToken), cancellationToken).ConfigureAwait(false))
            {
                confirmed.Add(candidate);
            }
        }

        if (confirmed.Count(f => !f.IsHardLinkOfAnother) < 2)
        {
            return null;
        }

        return new DuplicateGroup
        {
            Size = group.Size,
            Sha256 = group.Sha256,
            Files = confirmed,
            Verification = DuplicateVerification.ByteForByte,
        };
    }

    private static bool ContentEquals(DuplicateFile left, DuplicateFile right, ScanState state, CancellationToken cancellationToken)
    {
        state.Report(DuplicateScanStage.Verifying, right.Path);
        var a = ArrayPool<byte>.Shared.Rent(ReadBuffer);
        var b = ArrayPool<byte>.Shared.Rent(ReadBuffer);
        try
        {
            using var first = OpenForRead(left, state);
            using var second = OpenForRead(right, state);
            if (first is null || second is null)
            {
                return false;
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var readA = first.ReadAtLeast(a, ReadBuffer, throwOnEndOfStream: false);
                var readB = second.ReadAtLeast(b, ReadBuffer, throwOnEndOfStream: false);
                if (readA != readB || !a.AsSpan(0, readA).SequenceEqual(b.AsSpan(0, readB)))
                {
                    return false;
                }

                if (readA == 0)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(a);
            ArrayPool<byte>.Shared.Return(b);
        }
    }

    private static FileStream? OpenForRead(DuplicateFile file, ScanState state)
    {
        try
        {
            return new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state.MarkUnreadable();
            return null;
        }
    }

    private sealed class ScanState
    {
        private readonly IProgress<DuplicateScanProgress>? _progress;
        private long _bytesRead;
        private long _lastReport;
        private int _unreadable;

        public ScanState(IProgress<DuplicateScanProgress>? progress) => _progress = progress;

        public long FilesFound { get; set; }

        public long Candidates { get; set; }

        public long BytesToRead { get; set; }

        public int CloudSkipped { get; set; }

        public int Unreadable => Volatile.Read(ref _unreadable);

        public void MarkUnreadable() => Interlocked.Increment(ref _unreadable);

        public long BytesRead
        {
            get => Interlocked.Read(ref _bytesRead);
            set => Interlocked.Exchange(ref _bytesRead, value);
        }

        public void AddRead(int count) => Interlocked.Add(ref _bytesRead, count);

        public void Report(DuplicateScanStage stage, string? path)
        {
            if (_progress is null)
            {
                return;
            }

            var now = Environment.TickCount64;
            if (stage != DuplicateScanStage.Done && path is not null && now - Interlocked.Read(ref _lastReport) < 50)
            {
                return;
            }

            Interlocked.Exchange(ref _lastReport, now);
            _progress.Report(new DuplicateScanProgress(stage, FilesFound, Candidates, BytesToRead, BytesRead, path));
        }
    }
}
