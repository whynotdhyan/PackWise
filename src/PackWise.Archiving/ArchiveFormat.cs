namespace PackWise.Archiving;

public enum FormatCategory
{
    Archive,
    CompressedStream,
    DiskImage,
    Installer,
    FileSystem,
    Other,
}

/// <summary>
/// Describes what PackWise can honestly do with a format handler shipped by the engine.
/// </summary>
public sealed class ArchiveFormat
{
    internal ArchiveFormat(
        Guid classId,
        string name,
        string displayName,
        IReadOnlyList<string> extensions,
        IReadOnlyList<byte[]> signatures,
        int signatureOffset,
        FormatCategory category,
        bool isTrusted,
        bool canCreate,
        bool canUpdate,
        bool canReadEncrypted,
        bool canCreateEncrypted,
        bool canEncryptNames,
        bool supportsVolumes)
    {
        ClassId = classId;
        Name = name;
        DisplayName = displayName;
        Extensions = extensions;
        Signatures = signatures;
        SignatureOffset = signatureOffset;
        Category = category;
        IsTrusted = isTrusted;
        CanCreate = canCreate;
        CanUpdate = canUpdate;
        CanReadEncrypted = canReadEncrypted;
        CanCreateEncrypted = canCreateEncrypted;
        CanEncryptNames = canEncryptNames;
        SupportsVolumes = supportsVolumes;
    }

    internal Guid ClassId { get; }

    /// <summary>Engine handler name, for example "7z" or "Rar5".</summary>
    public string Name { get; }

    public string DisplayName { get; }

    public IReadOnlyList<string> Extensions { get; }

    internal IReadOnlyList<byte[]> Signatures { get; }

    internal int SignatureOffset { get; }

    public FormatCategory Category { get; }

    /// <summary>
    /// Trusted formats are opened automatically. Others (disk images, file systems,
    /// executables) widen the parser attack surface and must be enabled in settings.
    /// </summary>
    public bool IsTrusted { get; }

    /// <summary>PackWise offers new archives in this format.</summary>
    public bool CanCreate { get; }

    /// <summary>The engine can add or remove entries in an existing archive.</summary>
    public bool CanUpdate { get; }

    public bool CanReadEncrypted { get; }

    public bool CanCreateEncrypted { get; }

    public bool CanEncryptNames { get; }

    public bool SupportsVolumes { get; }

    public bool CanExtract => true;

    public string PrimaryExtension => Extensions.Count > 0 ? Extensions[0] : string.Empty;

    public bool MatchesExtension(string extension) =>
        Extensions.Contains(extension.TrimStart('.').ToLowerInvariant(), StringComparer.Ordinal);

    internal bool MatchesSignature(ReadOnlySpan<byte> header)
    {
        foreach (var signature in Signatures)
        {
            if (header.Length >= SignatureOffset + signature.Length &&
                header.Slice(SignatureOffset, signature.Length).SequenceEqual(signature))
            {
                return true;
            }
        }

        return false;
    }

    public override string ToString() => DisplayName;
}
