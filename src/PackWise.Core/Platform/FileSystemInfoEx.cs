using Microsoft.Win32.SafeHandles;

namespace PackWise.Core.Platform;

/// <summary>Identifies the physical file behind a path (hard links share an identity).</summary>
public readonly record struct FileIdentity(uint VolumeSerial, ulong FileIndex, uint LinkCount);

public static class FileSystemInfoEx
{
    /// <summary>OneDrive and other cloud providers mark online-only placeholders with these attributes.</summary>
    public const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    public const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    public static bool IsCloudPlaceholder(FileAttributes attributes) =>
        (attributes & (RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline)) != 0;

    public static FileIdentity? GetIdentity(SafeFileHandle handle) =>
        NativeMethods.GetFileInformationByHandle(handle, out var info)
            ? new FileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow, info.NumberOfLinks)
            : null;

    /// <summary>Free space available to the current user at a path, or null when unknown.</summary>
    public static ulong? GetFreeSpace(string path)
    {
        try
        {
            var directory = Path.GetFullPath(path);
            while (!Directory.Exists(directory))
            {
                var parent = Path.GetDirectoryName(directory);
                if (parent is null)
                {
                    return null;
                }

                directory = parent;
            }

            return NativeMethods.GetDiskFreeSpaceEx(directory.EndsWith('\\') ? directory : directory + "\\", out var free, out _, out _) ? free : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the first existing directory strictly below <paramref name="root"/> and above
    /// <paramref name="path"/> that is a symbolic link or junction. The root itself is the
    /// user's choice and is not inspected.
    /// </summary>
    public static string? FindReparsePointBetween(string root, string path, HashSet<string> verifiedSafe)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var current = Path.GetDirectoryName(Path.GetFullPath(path));
        var chain = new List<string>();

        while (current is not null && current.Length > rootFull.Length && IsWithin(rootFull, current))
        {
            chain.Add(current);
            current = Path.GetDirectoryName(current);
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var directory = chain[i];
            if (verifiedSafe.Contains(directory))
            {
                continue;
            }

            try
            {
                var info = new DirectoryInfo(directory);
                if (!info.Exists)
                {
                    // Everything below a missing directory will be created by PackWise itself.
                    break;
                }

                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return directory;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return directory;
            }

            verifiedSafe.Add(directory);
        }

        return null;
    }

    public static bool IsWithin(string root, string candidate)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var candidateFull = Path.GetFullPath(candidate);
        return candidateFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns "name (2).ext", "name (3).ext"… until the path is free.</summary>
    public static string MakeUnique(string path, Func<string, bool>? isTaken = null)
    {
        isTaken ??= p => File.Exists(p) || Directory.Exists(p);
        if (!isTaken(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var extension = Path.GetExtension(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        for (var n = 2; n < 100_000; n++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({n}){extension}");
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find a free name for “{path}”.");
    }

    public static string? GetDriveRoot(string path)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(path));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
