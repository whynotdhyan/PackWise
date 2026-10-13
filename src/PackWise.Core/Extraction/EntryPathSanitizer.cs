using System.Text;

namespace PackWise.Core.Extraction;

[Flags]
public enum PathIssues
{
    None = 0,
    AbsolutePath = 1 << 0,
    DriveOrNetworkPrefix = 1 << 1,
    ParentTraversal = 1 << 2,
    AlternateDataStream = 1 << 3,
    ReservedDeviceName = 1 << 4,
    InvalidCharacters = 1 << 5,
    TrailingDotOrSpace = 1 << 6,
    ControlCharacters = 1 << 7,
    DirectionOverride = 1 << 8,
    NameTooLong = 1 << 9,
    Empty = 1 << 10,
}

/// <summary>A cleaned relative path, or a rejection when the entry cannot be placed safely.</summary>
public sealed record SanitizedPath(string? RelativePath, PathIssues Issues)
{
    public bool IsRejected => RelativePath is null;

    /// <summary>Issues that change where or how a file lands, worth surfacing to the user.</summary>
    public bool WasRewritten => Issues != PathIssues.None && !IsRejected;
}

/// <summary>
/// Turns untrusted archive entry names into safe relative Windows paths.
/// Rejects anything that tries to climb out of the destination; rewrites names Windows
/// would misinterpret (streams, devices, trailing dots) instead of passing them through.
/// </summary>
public static class EntryPathSanitizer
{
    public const int MaxComponentLength = 255;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    private static readonly char[] InvalidNameChars = ['<', '>', '"', '|', '?', '*'];

    public static SanitizedPath Sanitize(string rawPath)
    {
        ArgumentNullException.ThrowIfNull(rawPath);
        var issues = PathIssues.None;
        var path = rawPath.Replace('/', '\\');

        path = StripPrefixes(path, ref issues);

        var components = new List<string>();
        foreach (var part in path.Split('\\'))
        {
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            if (part.Trim() == "..")
            {
                return new SanitizedPath(null, issues | PathIssues.ParentTraversal);
            }

            var clean = SanitizeComponent(part, ref issues);
            if (clean.Length > 0)
            {
                components.Add(clean);
            }
        }

        return components.Count == 0
            ? new SanitizedPath(null, issues | PathIssues.Empty)
            : new SanitizedPath(string.Join('\\', components), issues);
    }

    /// <summary>Sanitises a single file name typed by the user or derived from an archive name.</summary>
    public static string SanitizeFileName(string name)
    {
        var issues = PathIssues.None;
        var clean = SanitizeComponent(name.Replace('\\', '_').Replace('/', '_'), ref issues);
        return clean.Length == 0 ? "_" : clean;
    }

    private static string StripPrefixes(string path, ref PathIssues issues)
    {
        // Device and long-path prefixes: \\?\, \\.\, \??\
        foreach (var prefix in new[] { @"\\?\UNC\", @"\\?\", @"\\.\", @"\??\" })
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[prefix.Length..];
                issues |= PathIssues.DriveOrNetworkPrefix;
                break;
            }
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            issues |= PathIssues.DriveOrNetworkPrefix;
            path = path.TrimStart('\\');
        }

        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]))
        {
            issues |= PathIssues.DriveOrNetworkPrefix;
            path = path[2..];
        }

        if (path.StartsWith('\\'))
        {
            issues |= PathIssues.AbsolutePath;
            path = path.TrimStart('\\');
        }

        return path;
    }

    private static string SanitizeComponent(string component, ref PathIssues issues)
    {
        var builder = new StringBuilder(component.Length);
        foreach (var c in component)
        {
            if (c < 0x20 || c == 0x7F)
            {
                issues |= PathIssues.ControlCharacters;
                builder.Append('_');
            }
            else if (c == ':')
            {
                issues |= PathIssues.AlternateDataStream;
                builder.Append('_');
            }
            else if (Array.IndexOf(InvalidNameChars, c) >= 0)
            {
                issues |= PathIssues.InvalidCharacters;
                builder.Append('_');
            }
            else if (IsDirectionalFormatting(c))
            {
                issues |= PathIssues.DirectionOverride;
            }
            else
            {
                builder.Append(c);
            }
        }

        var name = builder.ToString();
        var trimmed = name.TrimEnd('.', ' ');
        if (trimmed.Length != name.Length)
        {
            issues |= PathIssues.TrailingDotOrSpace;
            name = trimmed.Length == 0 ? "_" : trimmed;
        }

        if (IsReservedDeviceName(name))
        {
            issues |= PathIssues.ReservedDeviceName;
            name = "_" + name;
        }

        if (name.Length > MaxComponentLength)
        {
            issues |= PathIssues.NameTooLong;
            name = Shorten(name);
        }

        return name;
    }

    public static bool IsReservedDeviceName(string name)
    {
        var dot = name.IndexOf('.');
        var stem = (dot >= 0 ? name[..dot] : name).TrimEnd(' ');
        return ReservedNames.Contains(stem);
    }

    private static bool IsDirectionalFormatting(char c) =>
        c is '\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');

    private static string Shorten(string name)
    {
        var extension = Path.GetExtension(name);
        if (extension.Length > 32)
        {
            extension = string.Empty;
        }

        var stem = name[..^extension.Length];
        return stem[..(MaxComponentLength - extension.Length)] + extension;
    }
}
