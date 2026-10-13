namespace PackWise.Archiving;

/// <summary>A file or folder on disk to be stored in an archive under <see cref="ArchivePath"/>.</summary>
public sealed record ArchiveSourceItem(
    string SourcePath,
    string ArchivePath,
    bool IsDirectory,
    ulong Size,
    FileAttributes Attributes,
    DateTime CreatedUtc,
    DateTime ModifiedUtc,
    DateTime AccessedUtc)
{
    public static ArchiveSourceItem FromFileSystemInfo(FileSystemInfo info, string archivePath) => new(
        info.FullName,
        archivePath,
        info is DirectoryInfo,
        info is FileInfo file ? (ulong)file.Length : 0,
        info.Attributes,
        info.CreationTimeUtc,
        info.LastWriteTimeUtc,
        info.LastAccessTimeUtc);

    /// <summary>
    /// Expands files and folders into archive items. Folder contents are stored under the
    /// folder's own name. Reparse points (symlinks, junctions) are never followed.
    /// </summary>
    public static List<ArchiveSourceItem> Collect(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var items = new List<ArchiveSourceItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in paths)
        {
            var path = Path.GetFullPath(raw).TrimEnd('\\');
            if (Directory.Exists(path))
            {
                var root = new DirectoryInfo(path);
                AddDirectory(root, root.Name, items, seen, cancellationToken);
            }
            else if (File.Exists(path))
            {
                var file = new FileInfo(path);
                if (seen.Add(file.Name))
                {
                    items.Add(FromFileSystemInfo(file, file.Name));
                }
            }
        }

        return items;
    }

    private static void AddDirectory(DirectoryInfo directory, string archivePath, List<ArchiveSourceItem> items, HashSet<string> seen, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!seen.Add(archivePath))
        {
            return;
        }

        items.Add(FromFileSystemInfo(directory, archivePath));

        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
        };

        foreach (var child in directory.EnumerateFileSystemInfos("*", options))
        {
            var childPath = archivePath + "\\" + child.Name;
            if (child is DirectoryInfo childDirectory)
            {
                AddDirectory(childDirectory, childPath, items, seen, cancellationToken);
            }
            else if (seen.Add(childPath))
            {
                items.Add(FromFileSystemInfo(child, childPath));
            }
        }
    }
}

public enum CompressionPreset
{
    Store = 0,
    Fastest = 1,
    Fast = 3,
    Balanced = 5,
    Maximum = 7,
    Smallest = 9,
}

public sealed class CreateArchiveOptions
{
    public required ArchiveFormat Format { get; init; }

    public CompressionPreset Level { get; init; } = CompressionPreset.Balanced;

    /// <summary>Encrypts content with AES-256. Never logged or persisted.</summary>
    public string? Password { get; init; }

    /// <summary>7z only: also encrypts the file list so names are hidden without the password.</summary>
    public bool EncryptFileNames { get; init; }

    /// <summary>7z only: compress files as one block for a better ratio.</summary>
    public bool Solid { get; init; } = true;

    public int? ThreadCount { get; init; }
}
