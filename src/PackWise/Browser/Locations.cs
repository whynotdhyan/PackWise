namespace PackWise.Browser;

/// <summary>Where the file list is pointing. Locations are values so they can live in history.</summary>
public abstract record Location
{
    public abstract string DisplayPath { get; }
}

public sealed record ComputerLocation : Location
{
    public static ComputerLocation Instance { get; } = new();

    public override string DisplayPath => "This PC";
}

public sealed record FolderLocation(string Path) : Location
{
    public override string DisplayPath => Path;
}

/// <summary>
/// A folder inside an archive. Archives opened from inside other archives point at a private
/// temporary copy and remember the outer location they came from.
/// </summary>
public sealed record ArchiveLocation(string ArchivePath, string InnerPath, string DisplayRoot, Location? Parent = null) : Location
{
    public override string DisplayPath => InnerPath.Length == 0 ? DisplayRoot : $"{DisplayRoot}\\{InnerPath}";

    public bool IsNested => Parent is ArchiveLocation;

    public ArchiveLocation WithInnerPath(string innerPath) => this with { InnerPath = innerPath };

    /// <summary>The file system folder that holds the outermost archive.</summary>
    public string HostFolder => Parent switch
    {
        ArchiveLocation outer => outer.HostFolder,
        FolderLocation folder => folder.Path,
        _ => System.IO.Path.GetDirectoryName(ArchivePath) ?? string.Empty,
    };
}

/// <summary>One clickable segment in the breadcrumb bar.</summary>
public sealed record Breadcrumb(string Label, Location Target, bool IsArchive);
