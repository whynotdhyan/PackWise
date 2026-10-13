using PackWise.Archiving;
using PackWise.Core.Platform;
using PackWise.Core.Security;

namespace PackWise.Core.Extraction;

/// <summary>
/// Decides, before a single byte is written, where every selected entry will land and what
/// happens to collisions. Execution never computes paths on its own.
/// </summary>
public static class ExtractionPlanner
{
    public static ExtractionPlan Create(
        IReadOnlyList<ArchiveEntry> entries,
        ExtractionRequest request,
        IConflictResolver? resolver = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(request);

        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
        var baseFolder = request.BaseFolder.Trim('\\');
        var selection = request.SelectedIndices;
        var claimed = new Dictionary<string, PlannedEntry>(StringComparer.OrdinalIgnoreCase);
        var verifiedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<PlannedEntry>();
        ConflictChoice? rememberedChoice = PolicyToChoice(request.ConflictPolicy);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selection is not null && !selection.Contains(entry.Index))
            {
                continue;
            }

            var relative = RelativeTo(entry.Path, baseFolder);
            if (relative is null)
            {
                continue;
            }

            if (!request.PreservePaths)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }

                relative = entry.Name;
            }

            var sanitized = EntryPathSanitizer.Sanitize(relative);
            var item = new PlannedEntry
            {
                Entry = entry,
                PathIssues = sanitized.Issues,
                Risk = entry.IsDirectory ? FileRisk.None : FileRiskClassifier.Classify(entry.Name),
                Action = entry.IsDirectory ? PlanAction.CreateDirectory : PlanAction.Write,
            };
            items.Add(item);

            if (sanitized.IsRejected)
            {
                Skip(item, SkipReason.UnsafePath);
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(destination, sanitized.RelativePath!));
            if (!FileSystemInfoEx.IsWithin(destination, target))
            {
                Skip(item, SkipReason.UnsafePath);
                continue;
            }

            item.TargetPath = target;

            if (entry.IsLink)
            {
                Skip(item, SkipReason.LinkEntry);
                continue;
            }

            if (entry.IsAlternateStream)
            {
                Skip(item, SkipReason.AlternateStream);
                continue;
            }

            if (FileSystemInfoEx.FindReparsePointBetween(destination, target, verifiedDirectories) is not null)
            {
                Skip(item, SkipReason.DestinationLink);
                continue;
            }

            if (entry.IsDirectory)
            {
                claimed.TryAdd(target, item);
                continue;
            }

            var existsOnDisk = File.Exists(target) || Directory.Exists(target);
            var claimedByEarlier = claimed.TryGetValue(target, out var earlier) && earlier.Action == PlanAction.Write;
            if (existsOnDisk || claimedByEarlier || claimed.ContainsKey(target))
            {
                var choice = rememberedChoice;
                if (choice is null)
                {
                    var resolution = resolver?.Resolve(Describe(entry, target)) ?? new ConflictResolution(ConflictChoice.KeepBoth, false);
                    choice = resolution.Choice;
                    if (resolution.ApplyToAll)
                    {
                        rememberedChoice = choice;
                    }
                }

                if (choice == ConflictChoice.Cancel)
                {
                    return new ExtractionPlan(destination, items, canceled: true);
                }

                var targetIsDirectory = Directory.Exists(target) || (claimed.TryGetValue(target, out var owner) && owner.Action == PlanAction.CreateDirectory);
                if (choice == ConflictChoice.Replace && !targetIsDirectory)
                {
                    item.ReplacesExisting = existsOnDisk;
                    if (claimedByEarlier)
                    {
                        // A later entry with the same name wins; the earlier one is not written at all.
                        Skip(earlier!, SkipReason.ExistingKept);
                    }
                }
                else if (choice == ConflictChoice.Skip)
                {
                    Skip(item, SkipReason.ExistingKept);
                    continue;
                }
                else
                {
                    target = FileSystemInfoEx.MakeUnique(target, p => File.Exists(p) || Directory.Exists(p) || claimed.ContainsKey(p));
                    item.TargetPath = target;
                    item.Renamed = true;
                }
            }

            claimed[target] = item;
        }

        return new ExtractionPlan(destination, items, canceled: false);
    }

    private static ConflictInfo Describe(ArchiveEntry entry, string target)
    {
        var file = new FileInfo(target);
        return file.Exists
            ? new ConflictInfo(entry, target, false, file.Length, file.LastWriteTimeUtc)
            : new ConflictInfo(entry, target, Directory.Exists(target), null, null);
    }

    private static void Skip(PlannedEntry item, SkipReason reason)
    {
        item.Action = PlanAction.Skip;
        item.SkipReason = reason;
    }

    private static ConflictChoice? PolicyToChoice(ConflictPolicy policy) => policy switch
    {
        ConflictPolicy.Replace => ConflictChoice.Replace,
        ConflictPolicy.Skip => ConflictChoice.Skip,
        ConflictPolicy.KeepBoth => ConflictChoice.KeepBoth,
        _ => null,
    };

    private static string? RelativeTo(string path, string baseFolder)
    {
        if (baseFolder.Length == 0)
        {
            return path;
        }

        return path.Length > baseFolder.Length && path[baseFolder.Length] == '\\' && path.StartsWith(baseFolder, StringComparison.OrdinalIgnoreCase)
            ? path[(baseFolder.Length + 1)..]
            : null;
    }
}
