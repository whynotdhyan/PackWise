namespace PackWise.Archiving;

/// <summary>One item listed by an archive handler. Paths are exactly as stored and untrusted.</summary>
public sealed class ArchiveEntry
{
    public required int Index { get; init; }

    /// <summary>Raw path from the archive with separators normalised to '\'. Never use it as a file system path directly.</summary>
    public required string Path { get; init; }

    public required bool IsDirectory { get; init; }

    public ulong? Size { get; init; }

    public ulong? PackedSize { get; init; }

    public DateTime? Modified { get; init; }

    public DateTime? Created { get; init; }

    public uint? Attributes { get; init; }

    public uint? Crc { get; init; }

    public bool IsEncrypted { get; init; }

    public string? Method { get; init; }

    public string? Comment { get; init; }

    /// <summary>Target of a symbolic or hard link entry. PackWise never materialises links.</summary>
    public string? LinkTarget { get; init; }

    public bool IsLink { get; init; }

    public bool IsAlternateStream { get; init; }

    public string Name
    {
        get
        {
            var trimmed = Path.TrimEnd('\\');
            var slash = trimmed.LastIndexOf('\\');
            return slash < 0 ? trimmed : trimmed[(slash + 1)..];
        }
    }
}

[Flags]
public enum ArchiveIssues
{
    None = 0,
    NotArchive = 1 << 0,
    HeadersError = 1 << 1,
    EncryptedHeadersError = 1 << 2,
    UnavailableStart = 1 << 3,
    UnconfirmedStart = 1 << 4,
    UnexpectedEnd = 1 << 5,
    DataAfterEnd = 1 << 6,
    UnsupportedMethod = 1 << 7,
    UnsupportedFeature = 1 << 8,
    DataError = 1 << 9,
    CrcError = 1 << 10,
}

public sealed class ArchiveInfo
{
    public required string FilePath { get; init; }

    public required ArchiveFormat Format { get; init; }

    /// <summary>Formats traversed to reach the listing, outermost first (for example Split → 7z).</summary>
    public required IReadOnlyList<ArchiveFormat> FormatChain { get; init; }

    public ulong? PhysicalSize { get; init; }

    public ulong? HeadersSize { get; init; }

    public bool IsSolid { get; init; }

    public int VolumeCount { get; init; } = 1;

    public bool IsMultiVolume => VolumeCount > 1;

    public string? Comment { get; init; }

    public string? Method { get; init; }

    public bool HasEncryptedHeaders { get; init; }

    public ulong? Offset { get; init; }

    public ArchiveIssues Errors { get; init; }

    public ArchiveIssues Warnings { get; init; }

    public string? ErrorMessage { get; init; }
}

public enum EntryResult
{
    Ok,
    Skipped,
    UnsupportedMethod,
    DataError,
    CrcError,
    WrongPassword,
    Unavailable,
    UnexpectedEnd,
    DataAfterEnd,
    HeadersError,
    NotArchive,
    SizeLimitExceeded,
    WriteFailed,
    Canceled,
}

public static class EntryResultExtensions
{
    public static bool IsSuccess(this EntryResult result) => result is EntryResult.Ok or EntryResult.Skipped;

    public static string Describe(this EntryResult result) => result switch
    {
        EntryResult.Ok => "OK",
        EntryResult.Skipped => "Skipped",
        EntryResult.UnsupportedMethod => "Compression method not supported",
        EntryResult.DataError => "Data error (damaged, or wrong password)",
        EntryResult.CrcError => "Checksum mismatch (damaged, or wrong password)",
        EntryResult.WrongPassword => "Wrong password",
        EntryResult.Unavailable => "Data unavailable (missing volume?)",
        EntryResult.UnexpectedEnd => "Unexpected end of data (incomplete download?)",
        EntryResult.DataAfterEnd => "Unexpected data after the end of the item",
        EntryResult.HeadersError => "Damaged headers",
        EntryResult.NotArchive => "Not a valid archive",
        EntryResult.SizeLimitExceeded => "Stopped: output exceeded the safety limit",
        EntryResult.WriteFailed => "Could not write the output file",
        EntryResult.Canceled => "Canceled",
        _ => result.ToString(),
    };
}

public readonly record struct ArchiveProgress(ulong CompletedBytes, ulong TotalBytes, string? CurrentItem);

/// <summary>Supplies passwords on demand. Returning null cancels the request.</summary>
public interface IPasswordProvider
{
    string? GetPassword(string archivePath, bool previousAttemptFailed);
}

public sealed class ArchiveOpenOptions
{
    public static ArchiveOpenOptions Default { get; } = new();

    /// <summary>Open formats outside the trusted set (disk images, file systems, executables).</summary>
    public bool AllowUntrustedFormats { get; init; }

    public IPasswordProvider? PasswordProvider { get; init; }

    /// <summary>A known password tried before asking the provider.</summary>
    public string? Password { get; init; }
}

/// <summary>
/// Receives extracted data. The engine never touches the file system on extraction;
/// the target decides where (and whether) each entry is written.
/// </summary>
public interface IExtractTarget
{
    /// <summary>Returns a writable stream for the entry, or null to skip it.</summary>
    Stream? OpenEntry(ArchiveEntry entry);

    /// <summary>Upper bound on bytes the engine may write for this entry.</summary>
    long GetByteLimit(ArchiveEntry entry);

    /// <summary>Called once per opened entry with the outcome; the target owns closing the stream.</summary>
    void CompleteEntry(ArchiveEntry entry, Stream stream, EntryResult result, long bytesWritten);
}

public sealed record EntryOutcome(ArchiveEntry Entry, EntryResult Result);
