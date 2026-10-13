using PackWise.Core.Platform;

namespace PackWise.Core.Duplicates;

public sealed class DuplicateScanOptions
{
    public required IReadOnlyList<string> Roots { get; init; }

    /// <summary>Files smaller than this are ignored. Empty files are never reported.</summary>
    public long MinimumSize { get; init; } = 1;

    public bool IncludeHiddenFiles { get; init; }

    /// <summary>Scan Windows, Program Files, ProgramData and other system locations.</summary>
    public bool IncludeSystemFolders { get; init; }

    /// <summary>
    /// Leave online-only cloud files (OneDrive "Files On-Demand") alone. Reading them would
    /// download every file, so they are skipped and counted instead.
    /// </summary>
    public bool SkipCloudPlaceholders { get; init; } = true;

    /// <summary>Compare matching files byte by byte after hashing.</summary>
    public bool VerifyByteByByte { get; init; }

    public IReadOnlyList<string> ExcludedFolders { get; init; } = [];

    public int MaxDegreeOfParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
}

public enum DuplicateScanStage
{
    Enumerating,
    Sampling,
    Hashing,
    Verifying,
    Done,
}

public readonly record struct DuplicateScanProgress(
    DuplicateScanStage Stage,
    long FilesFound,
    long CandidateFiles,
    long BytesToRead,
    long BytesRead,
    string? CurrentPath);

public enum DuplicateVerification
{
    /// <summary>Same size and identical SHA-256 of the full content.</summary>
    ContentHash,

    /// <summary>Additionally compared byte by byte.</summary>
    ByteForByte,
}

public sealed class DuplicateFile
{
    public required string Path { get; init; }

    public required long Size { get; init; }

    public required DateTime ModifiedUtc { get; init; }

    public required DateTime CreatedUtc { get; init; }

    public FileIdentity? Identity { get; internal set; }

    /// <summary>True when this path is a hard link to another file in the same group (no space to reclaim).</summary>
    public bool IsHardLinkOfAnother { get; internal set; }

    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public string Name => System.IO.Path.GetFileName(Path);
}

public sealed class DuplicateGroup
{
    public required long Size { get; init; }

    public required string Sha256 { get; init; }

    public required IReadOnlyList<DuplicateFile> Files { get; init; }

    public DuplicateVerification Verification { get; internal set; }

    public int PhysicalCopies => Files.Count(f => !f.IsHardLinkOfAnother);

    /// <summary>Space that removing all but one physical copy would free (an estimate: compression and links can change it).</summary>
    public long ReclaimableBytes => Size * Math.Max(0, PhysicalCopies - 1);
}

public sealed class DuplicateScanResult
{
    public required IReadOnlyList<DuplicateGroup> Groups { get; init; }

    public long FilesScanned { get; init; }

    public long BytesHashed { get; init; }

    public int SkippedCloudFiles { get; init; }

    public int UnreadableFiles { get; init; }

    public bool Canceled { get; init; }

    public TimeSpan Duration { get; init; }

    public long ReclaimableBytes => Groups.Sum(g => g.ReclaimableBytes);
}
