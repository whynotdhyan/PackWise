namespace PackWise.Core.Security;

public enum MarkOfTheWebPolicy
{
    /// <summary>Copy the archive's internet-origin mark to every extracted file (default).</summary>
    AllFiles,

    /// <summary>Copy the mark only to files that can run code or contain macros.</summary>
    RiskyFilesOnly,

    Off,
}

/// <summary>
/// Reads and writes the Zone.Identifier stream Windows uses to remember that a file came
/// from the internet. Propagating it keeps SmartScreen and Office Protected View working
/// for files that arrive inside downloaded archives.
/// </summary>
public static class MarkOfTheWeb
{
    private const string StreamSuffix = ":Zone.Identifier";
    private const int MaxStreamLength = 64 * 1024;

    /// <summary>Returns the raw Zone.Identifier content when the file carries an internet or untrusted zone.</summary>
    public static string? Read(string path)
    {
        try
        {
            var streamPath = path + StreamSuffix;
            if (!File.Exists(streamPath))
            {
                return null;
            }

            using var stream = new FileStream(streamPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > MaxStreamLength)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            return ZoneOf(content) >= 3 ? content : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Writes the mark. Fails silently on volumes without stream support (FAT32, some network shares).</summary>
    public static bool Write(string path, string zoneIdentifier)
    {
        try
        {
            File.WriteAllText(path + StreamSuffix, zoneIdentifier);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    public static string CreateInternetZone(string? hostUrl = null) =>
        hostUrl is null
            ? "[ZoneTransfer]\r\nZoneId=3\r\n"
            : $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl={hostUrl}\r\n";

    public static bool ShouldApply(MarkOfTheWebPolicy policy, string fileName) => policy switch
    {
        MarkOfTheWebPolicy.AllFiles => true,
        MarkOfTheWebPolicy.RiskyFilesOnly => FileRiskClassifier.CanRunCode(FileRiskClassifier.Classify(fileName)),
        _ => false,
    };

    internal static int ZoneOf(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(trimmed.AsSpan(7), out var zone))
            {
                return zone;
            }
        }

        return -1;
    }
}
