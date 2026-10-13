using PackWise.Archiving;
using PackWise.Core.Security;

namespace PackWise.Core.Extraction;

public enum ConflictPolicy
{
    Ask,
    Replace,
    Skip,
    KeepBoth,
}

public enum ConflictChoice
{
    Replace,
    Skip,
    KeepBoth,
    Cancel,
}

public sealed record ConflictInfo(ArchiveEntry Entry, string TargetPath, bool TargetIsDirectory, long? ExistingSize, DateTime? ExistingModifiedUtc);

public readonly record struct ConflictResolution(ConflictChoice Choice, bool ApplyToAll);

/// <summary>Decides name collisions. Called on the planning thread; UI implementations marshal to the dispatcher.</summary>
public interface IConflictResolver
{
    ConflictResolution Resolve(ConflictInfo conflict);
}

public enum PlanAction
{
    CreateDirectory,
    Write,
    Skip,
}

public enum SkipReason
{
    None,
    UnsafePath,
    LinkEntry,
    AlternateStream,
    ExistingKept,
    DestinationLink,
}

public sealed class PlannedEntry
{
    public required ArchiveEntry Entry { get; init; }

    public string? TargetPath { get; internal set; }

    public PlanAction Action { get; internal set; }

    public SkipReason SkipReason { get; internal set; }

    public PathIssues PathIssues { get; init; }

    public bool ReplacesExisting { get; internal set; }

    public bool Renamed { get; internal set; }

    public FileRisk Risk { get; init; }

    public bool IsBlocked => SkipReason is SkipReason.UnsafePath or SkipReason.LinkEntry or SkipReason.AlternateStream or SkipReason.DestinationLink;

    public string DescribeSkip() => SkipReason switch
    {
        SkipReason.UnsafePath => "Blocked: the stored path points outside the destination folder.",
        SkipReason.LinkEntry => "Blocked: links inside archives are never created.",
        SkipReason.AlternateStream => "Blocked: hidden NTFS data streams are not extracted.",
        SkipReason.ExistingKept => "Skipped: a file with this name already exists.",
        SkipReason.DestinationLink => "Blocked: the destination path passes through a link or junction.",
        _ => string.Empty,
    };
}

public sealed class ExtractionRequest
{
    public required string Destination { get; init; }

    /// <summary>Entry indices to extract (folders should contribute their descendants). Null extracts everything.</summary>
    public IReadOnlySet<int>? SelectedIndices { get; init; }

    /// <summary>Archive folder the selection is relative to, so extracting "a\b\c.txt" while browsing "a\b" yields "c.txt".</summary>
    public string BaseFolder { get; init; } = string.Empty;

    public bool PreservePaths { get; init; } = true;

    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.Ask;
}

public sealed class ExtractionPlan
{
    internal ExtractionPlan(string destination, IReadOnlyList<PlannedEntry> items, bool canceled)
    {
        Destination = destination;
        Items = items;
        Canceled = canceled;
    }

    public string Destination { get; }

    public IReadOnlyList<PlannedEntry> Items { get; }

    public bool Canceled { get; }

    public IEnumerable<PlannedEntry> Writes => Items.Where(i => i.Action == PlanAction.Write);

    public int FileCount => Writes.Count();

    public ulong TotalBytes => Writes.Aggregate(0UL, (sum, i) => sum + (i.Entry.Size ?? 0));

    public int BlockedCount => Items.Count(i => i.IsBlocked);

    public int RenamedCount => Items.Count(i => i.Action == PlanAction.Write && (i.Renamed || i.PathIssues != PathIssues.None));

    public int ReplaceCount => Writes.Count(i => i.ReplacesExisting);

    public int RiskyCount => Writes.Count(i => FileRiskClassifier.CanRunCode(i.Risk) || i.Risk.HasFlag(FileRisk.SpoofedName));

    public bool NeedsPassword => Writes.Any(i => i.Entry.IsEncrypted);
}
