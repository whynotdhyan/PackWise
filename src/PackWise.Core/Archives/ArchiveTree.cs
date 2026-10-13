using PackWise.Archiving;

namespace PackWise.Core.Archives;

/// <summary>A folder or file in the browsable view of an archive.</summary>
public sealed class ArchiveNode
{
    private readonly Dictionary<string, ArchiveNode> _children = new(StringComparer.OrdinalIgnoreCase);
    private ulong? _size;
    private ulong? _packedSize;
    private int? _fileCount;

    internal ArchiveNode(string name, string path, ArchiveNode? parent, bool isDirectory)
    {
        Name = name;
        Path = path;
        Parent = parent;
        IsDirectory = isDirectory;
    }

    public string Name { get; }

    /// <summary>Path inside the archive using '\' separators; empty for the root.</summary>
    public string Path { get; }

    public ArchiveNode? Parent { get; }

    public bool IsDirectory { get; private set; }

    /// <summary>The listed entry, or null for folders implied only by their contents.</summary>
    public ArchiveEntry? Entry { get; private set; }

    public IReadOnlyCollection<ArchiveNode> Children => _children.Values;

    public ulong Size => _size ??= IsDirectory ? Children.Aggregate(0UL, (sum, c) => sum + c.Size) : Entry?.Size ?? 0;

    public ulong PackedSize => _packedSize ??= IsDirectory ? Children.Aggregate(0UL, (sum, c) => sum + c.PackedSize) : Entry?.PackedSize ?? 0;

    public int FileCount => _fileCount ??= IsDirectory ? Children.Sum(c => c.IsDirectory ? c.FileCount : 1) : 1;

    public DateTime? Modified => Entry?.Modified;

    public bool IsEncrypted => Entry?.IsEncrypted ?? (IsDirectory && Children.Any(c => c.IsEncrypted));

    public ArchiveNode? Find(string path)
    {
        var node = this;
        foreach (var part in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!node._children.TryGetValue(part, out var child))
            {
                return null;
            }

            node = child;
        }

        return node;
    }

    /// <summary>All entry indices at or below this node.</summary>
    public IEnumerable<ArchiveEntry> DescendantEntries()
    {
        if (Entry is not null)
        {
            yield return Entry;
        }

        foreach (var child in Children)
        {
            foreach (var entry in child.DescendantEntries())
            {
                yield return entry;
            }
        }
    }

    internal ArchiveNode GetOrAddChild(string name, bool isDirectory)
    {
        if (!_children.TryGetValue(name, out var child))
        {
            var childPath = Path.Length == 0 ? name : Path + "\\" + name;
            child = new ArchiveNode(name, childPath, this, isDirectory);
            _children.Add(name, child);
        }
        else if (isDirectory)
        {
            child.IsDirectory = true;
        }

        return child;
    }

    internal void Attach(ArchiveEntry entry)
    {
        // When an archive lists the same path twice, the later entry wins, as on extraction.
        Entry = entry;
    }
}

public static class ArchiveTree
{
    public static ArchiveNode Build(IEnumerable<ArchiveEntry> entries)
    {
        var root = new ArchiveNode(string.Empty, string.Empty, null, isDirectory: true);
        foreach (var entry in entries)
        {
            var parts = entry.Path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var node = root;
            for (var i = 0; i < parts.Length; i++)
            {
                var isLast = i == parts.Length - 1;
                node = node.GetOrAddChild(DisplayName(parts[i]), isDirectory: !isLast || entry.IsDirectory);
            }

            node.Attach(entry);
        }

        return root;
    }

    /// <summary>Keeps raw names visible while making navigation-like names unambiguous.</summary>
    private static string DisplayName(string part) => part switch
    {
        "." => "[.]",
        ".." => "[..]",
        _ => part,
    };
}
