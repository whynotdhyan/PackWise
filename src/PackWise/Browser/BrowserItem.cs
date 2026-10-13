using System.Globalization;
using PackWise.Core.Archives;
using PackWise.Core.Files;
using PackWise.Core.Security;
using PackWise.Infrastructure;

namespace PackWise.Browser;

public enum BrowserItemType
{
    ParentLink,
    Drive,
    Folder,
    File,
    ArchiveFolder,
    ArchiveFile,
}

/// <summary>A row in the main list. Display strings are computed on demand for virtualised rows.</summary>
public sealed class BrowserItem
{
    private string? _typeName;

    public required BrowserItemType Type { get; init; }

    public required string Name { get; init; }

    /// <summary>File system path for files, folders and drives.</summary>
    public string? FullPath { get; init; }

    /// <summary>Archive node for items inside an archive.</summary>
    public ArchiveNode? Node { get; init; }

    public ulong? Size { get; init; }

    public ulong? PackedSize { get; init; }

    public DateTime? Modified { get; init; }

    public uint? Crc { get; init; }

    public bool IsEncrypted { get; init; }

    public bool IsHidden { get; init; }

    public string? DriveDetail { get; init; }

    public FileKind Kind { get; init; }

    public FileRisk Risk { get; init; }

    /// <summary>Folder path shown when listing search results from several folders.</summary>
    public string? Location { get; init; }

    public bool IsFolderLike => Type is BrowserItemType.ParentLink or BrowserItemType.Drive or BrowserItemType.Folder or BrowserItemType.ArchiveFolder;

    public bool IsInArchive => Type is BrowserItemType.ArchiveFile or BrowserItemType.ArchiveFolder;

    public bool IsRisky => FileRiskClassifier.CanRunCode(Risk) || Risk.HasFlag(FileRisk.SpoofedName);

    public string TypeName => _typeName ??= Type switch
    {
        BrowserItemType.ParentLink => "Parent folder",
        BrowserItemType.Drive => DriveDetail ?? "Drive",
        _ => Shell.TypeName(Name, IsFolderLike),
    };

    public string SizeText => Type switch
    {
        BrowserItemType.ParentLink => string.Empty,
        BrowserItemType.Folder => string.Empty,
        _ => Size is { } size ? ByteSize.Format(size) : string.Empty,
    };

    public string PackedText => PackedSize is { } packed && IsInArchive ? ByteSize.Format(packed) : string.Empty;

    public string RatioText => IsInArchive && Size is > 0 && PackedSize is { } packed ? ByteSize.FormatRatio(packed, Size.Value) : string.Empty;

    public string ModifiedText => Modified is { } modified ? modified.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : string.Empty;

    public string CrcText => Crc is { } crc ? crc.ToString("X8", CultureInfo.InvariantCulture) : string.Empty;

    public string? RiskText => IsRisky ? FileRiskClassifier.Describe(Risk) : null;

    public static BrowserItem Parent() => new() { Type = BrowserItemType.ParentLink, Name = "..", Kind = FileKind.Folder };

    /// <summary>Used as the accessible name of the row by screen readers.</summary>
    public override string ToString() => Type == BrowserItemType.ParentLink ? "Parent folder" : $"{Name}, {TypeName}";
}
