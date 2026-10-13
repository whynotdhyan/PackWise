namespace PackWise.Archiving.SevenZip;

/// <summary>
/// PackWise's policy for each engine handler: display name, category, whether it is
/// opened by default, and which operations are offered. The engine reports what it can
/// parse; this table decides what PackWise promises.
/// </summary>
internal static class FormatCatalog
{
    private sealed record Policy(string DisplayName, FormatCategory Category, bool Trusted);

    private static readonly Dictionary<string, Policy> Policies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["7z"] = new("7-Zip", FormatCategory.Archive, true),
        ["zip"] = new("ZIP", FormatCategory.Archive, true),
        ["Rar"] = new("RAR (1.5–4.x)", FormatCategory.Archive, true),
        ["Rar5"] = new("RAR5", FormatCategory.Archive, true),
        ["tar"] = new("TAR", FormatCategory.Archive, true),
        ["Cab"] = new("Cabinet", FormatCategory.Archive, true),
        ["Arj"] = new("ARJ", FormatCategory.Archive, true),
        ["lzh"] = new("LZH / LHA", FormatCategory.Archive, true),
        ["Cpio"] = new("CPIO", FormatCategory.Archive, true),
        ["wim"] = new("Windows Imaging (WIM)", FormatCategory.Archive, true),
        ["Split"] = new("Split volumes (.001)", FormatCategory.Archive, true),
        ["gzip"] = new("GZip", FormatCategory.CompressedStream, true),
        ["bzip2"] = new("BZip2", FormatCategory.CompressedStream, true),
        ["xz"] = new("XZ", FormatCategory.CompressedStream, true),
        ["zstd"] = new("Zstandard", FormatCategory.CompressedStream, true),
        ["lzma"] = new("LZMA", FormatCategory.CompressedStream, true),
        ["lzma86"] = new("LZMA86", FormatCategory.CompressedStream, true),
        ["Z"] = new("Unix compress (.Z)", FormatCategory.CompressedStream, true),
        ["Iso"] = new("ISO 9660 image", FormatCategory.DiskImage, true),
        ["Udf"] = new("UDF image", FormatCategory.DiskImage, false),
        ["Dmg"] = new("Apple disk image", FormatCategory.DiskImage, false),
        ["VHD"] = new("Virtual hard disk (VHD)", FormatCategory.DiskImage, false),
        ["VHDX"] = new("Virtual hard disk (VHDX)", FormatCategory.DiskImage, false),
        ["VMDK"] = new("VMware disk", FormatCategory.DiskImage, false),
        ["VDI"] = new("VirtualBox disk", FormatCategory.DiskImage, false),
        ["QCOW"] = new("QEMU disk", FormatCategory.DiskImage, false),
        ["xar"] = new("XAR / macOS package", FormatCategory.Installer, false),
        ["Nsis"] = new("NSIS installer", FormatCategory.Installer, false),
        ["Compound"] = new("Compound file (MSI, legacy Office)", FormatCategory.Installer, false),
        ["Rpm"] = new("RPM package", FormatCategory.Installer, false),
        ["Ar"] = new("AR / Debian package", FormatCategory.Installer, false),
        ["Chm"] = new("Compiled HTML Help", FormatCategory.Installer, false),
    };

    public static ArchiveFormat Describe(
        Guid classId,
        string name,
        IReadOnlyList<string> extensions,
        IReadOnlyList<byte[]> signatures,
        int signatureOffset,
        bool engineCanUpdate)
    {
        var policy = Policies.GetValueOrDefault(name) ?? new Policy(name, GuessCategory(name), false);
        var isZip = name.Equals("zip", StringComparison.OrdinalIgnoreCase);
        var is7z = name.Equals("7z", StringComparison.OrdinalIgnoreCase);
        var isRar = name.StartsWith("Rar", StringComparison.OrdinalIgnoreCase);

        return new ArchiveFormat(
            classId,
            name,
            policy.DisplayName,
            extensions,
            signatures,
            signatureOffset,
            policy.Category,
            policy.Trusted,
            canCreate: engineCanUpdate && (isZip || is7z),
            canUpdate: engineCanUpdate && (isZip || is7z),
            canReadEncrypted: isZip || is7z || isRar,
            canCreateEncrypted: engineCanUpdate && (isZip || is7z),
            canEncryptNames: engineCanUpdate && is7z,
            supportsVolumes: isRar || name.Equals("Split", StringComparison.OrdinalIgnoreCase));
    }

    private static FormatCategory GuessCategory(string name) => name.ToUpperInvariant() switch
    {
        "NTFS" or "FAT" or "EXT" or "HFS" or "APFS" or "SQUASHFS" or "CRAMFS" or "MBR" or "GPT" or "APM" or "LP" or "SPARSE" => FormatCategory.FileSystem,
        "PE" or "TE" or "ELF" or "MACHO" or "MUB" or "COFF" or "UEFIC" or "UEFIF" => FormatCategory.Installer,
        _ => FormatCategory.Other,
    };
}
