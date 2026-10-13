using PackWise.Core.Platform;

namespace PackWise.Core.Duplicates;

public enum KeepRule
{
    Newest,
    Oldest,
    ShortestPath,
    InPreferredFolder,
}

/// <summary>Suggests which copies to act on. Every rule keeps at least one copy per group.</summary>
public static class KeepRules
{
    public static IReadOnlySet<string> SelectRemovals(IEnumerable<DuplicateGroup> groups, KeepRule rule, string? preferredFolder = null)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var keep = ChooseKeeper(group, rule, preferredFolder);
            foreach (var file in group.Files)
            {
                if (!ReferenceEquals(file, keep) && !file.IsHardLinkOfAnother)
                {
                    selected.Add(file.Path);
                }
            }
        }

        return selected;
    }

    private static DuplicateFile ChooseKeeper(DuplicateGroup group, KeepRule rule, string? preferredFolder)
    {
        var physical = group.Files.Where(f => !f.IsHardLinkOfAnother).ToList();
        return rule switch
        {
            KeepRule.Oldest => physical.MinBy(f => f.ModifiedUtc)!,
            KeepRule.ShortestPath => physical.MinBy(f => f.Path.Length)!,
            KeepRule.InPreferredFolder when preferredFolder is not null =>
                physical.FirstOrDefault(f => FileSystemInfoEx.IsWithin(preferredFolder, f.Path)) ?? physical.MaxBy(f => f.ModifiedUtc)!,
            _ => physical.MaxBy(f => f.ModifiedUtc)!,
        };
    }
}

public sealed record DuplicateActionResult(int Succeeded, IReadOnlyList<(string Path, string Reason)> Failures);

/// <summary>Reversible clean-up actions. Permanent deletion is deliberately not offered here.</summary>
public static class DuplicateActions
{
    /// <summary>
    /// Moves files to the Recycle Bin after re-checking that each still matches what was scanned
    /// and that at least one copy of its group remains.
    /// </summary>
    public static DuplicateActionResult Recycle(IReadOnlyCollection<DuplicateFile> files, IReadOnlyCollection<DuplicateGroup> groups)
    {
        var failures = new List<(string, string)>();
        var approved = new List<string>();

        foreach (var file in files)
        {
            if (Validate(file, groups, files) is { } reason)
            {
                failures.Add((file.Path, reason));
            }
            else if (!RecycleBin.IsAvailableFor(file.Path))
            {
                failures.Add((file.Path, "This drive has no Recycle Bin. Use “Move to review folder” instead."));
            }
            else
            {
                approved.Add(file.Path);
            }
        }

        var succeeded = 0;
        foreach (var path in approved)
        {
            try
            {
                if (RecycleBin.Send([path]))
                {
                    succeeded++;
                }
                else
                {
                    failures.Add((path, "Windows did not move the file to the Recycle Bin."));
                }
            }
            catch (IOException ex)
            {
                failures.Add((path, ex.Message));
            }
        }

        return new DuplicateActionResult(succeeded, failures);
    }

    /// <summary>
    /// Moves files into a review folder, mirroring their original location so nothing collides
    /// and every file can be put back by hand.
    /// </summary>
    public static DuplicateActionResult MoveToReviewFolder(IReadOnlyCollection<DuplicateFile> files, IReadOnlyCollection<DuplicateGroup> groups, string reviewFolder)
    {
        var failures = new List<(string, string)>();
        var succeeded = 0;

        foreach (var file in files)
        {
            if (Validate(file, groups, files) is { } reason)
            {
                failures.Add((file.Path, reason));
                continue;
            }

            try
            {
                var root = Path.GetPathRoot(file.Path) ?? string.Empty;
                var relative = file.Path[root.Length..];
                var drive = root.TrimEnd('\\', ':').Replace('\\', '_').Replace(':', '_');
                var target = FileSystemInfoEx.MakeUnique(Path.Combine(reviewFolder, drive, relative));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file.Path, target);
                succeeded++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add((file.Path, ex.Message));
            }
        }

        return new DuplicateActionResult(succeeded, failures);
    }

    private static string? Validate(DuplicateFile file, IReadOnlyCollection<DuplicateGroup> groups, IReadOnlyCollection<DuplicateFile> batch)
    {
        var info = new FileInfo(file.Path);
        if (!info.Exists)
        {
            return "The file no longer exists.";
        }

        if (info.Length != file.Size || info.LastWriteTimeUtc != file.ModifiedUtc)
        {
            return "The file changed since the scan. Scan again before acting on it.";
        }

        var group = groups.FirstOrDefault(g => g.Files.Contains(file));
        if (group is not null && group.Files.Where(f => !f.IsHardLinkOfAnother).All(f => batch.Contains(f)))
        {
            return "Every copy in this group is selected; PackWise always keeps at least one.";
        }

        return null;
    }
}
