using System.Buffers;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PackWise.Core.Hashing;

[Flags]
public enum ChecksumAlgorithms
{
    None = 0,
    Crc32 = 1 << 0,
    Sha256 = 1 << 1,
    Sha512 = 1 << 2,
}

public sealed record FileChecksums(string Path, long Size, string? Crc32, string? Sha256, string? Sha512);

public enum ChecksumMatch
{
    Match,
    Mismatch,
    Missing,
    Unreadable,
}

public sealed record ChecksumVerification(string FileName, string ExpectedHash, string? ActualHash, ChecksumMatch Result);

/// <summary>
/// Computes integrity checksums in a single read. CRC32 detects accidental corruption only;
/// SHA-256 and SHA-512 are also suitable for verifying published downloads.
/// </summary>
public static partial class Checksums
{
    private const int BufferSize = 1024 * 1024;

    public static FileChecksums Compute(string path, ChecksumAlgorithms algorithms, IProgress<long>? bytesProgress = null, CancellationToken cancellationToken = default)
    {
        var crc = algorithms.HasFlag(ChecksumAlgorithms.Crc32) ? new Crc32() : null;
        using var sha256 = algorithms.HasFlag(ChecksumAlgorithms.Sha256) ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        using var sha512 = algorithms.HasFlag(ChecksumAlgorithms.Sha512) ? IncrementalHash.CreateHash(HashAlgorithmName.SHA512) : null;

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            long total = 0;
            int read;
            while ((read = stream.Read(buffer, 0, BufferSize)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var span = buffer.AsSpan(0, read);
                crc?.Append(span);
                sha256?.AppendData(span);
                sha512?.AppendData(span);
                total += read;
                bytesProgress?.Report(total);
            }

            return new FileChecksums(
                path,
                total,
                crc is null ? null : crc.GetCurrentHashAsUInt32().ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                sha256 is null ? null : Convert.ToHexStringLower(sha256.GetHashAndReset()),
                sha512 is null ? null : Convert.ToHexStringLower(sha512.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Formats results as a standard "hash *name" checksum file (sha256sum compatible).</summary>
    public static string ToChecksumFile(IEnumerable<FileChecksums> results, ChecksumAlgorithms algorithm, string baseDirectory)
    {
        var lines = results.Select(r =>
        {
            var hash = algorithm switch
            {
                ChecksumAlgorithms.Sha512 => r.Sha512,
                ChecksumAlgorithms.Crc32 => r.Crc32,
                _ => r.Sha256,
            };
            var name = Path.GetRelativePath(baseDirectory, r.Path);
            return $"{hash} *{name}";
        });

        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>Checks files listed in a .sha256/.sha512 style file (GNU or BSD format) relative to its folder.</summary>
    public static IReadOnlyList<ChecksumVerification> Verify(string checksumFile, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(checksumFile))!;
        var results = new List<ChecksumVerification>();

        foreach (var line in File.ReadLines(checksumFile))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryParseLine(line, out var expected, out var name))
            {
                continue;
            }

            var algorithm = expected.Length switch
            {
                8 => ChecksumAlgorithms.Crc32,
                64 => ChecksumAlgorithms.Sha256,
                128 => ChecksumAlgorithms.Sha512,
                _ => ChecksumAlgorithms.None,
            };

            var path = Path.GetFullPath(Path.Combine(directory, name));
            if (algorithm == ChecksumAlgorithms.None || !Platform.FileSystemInfoEx.IsWithin(directory, path))
            {
                continue;
            }

            progress?.Report(name);
            if (!File.Exists(path))
            {
                results.Add(new ChecksumVerification(name, expected, null, ChecksumMatch.Missing));
                continue;
            }

            try
            {
                var computed = Compute(path, algorithm, cancellationToken: cancellationToken);
                var actual = computed.Sha256 ?? computed.Sha512 ?? computed.Crc32;
                var match = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                results.Add(new ChecksumVerification(name, expected, actual, match ? ChecksumMatch.Match : ChecksumMatch.Mismatch));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new ChecksumVerification(name, expected, null, ChecksumMatch.Unreadable));
            }
        }

        return results;
    }

    internal static bool TryParseLine(string line, out string hash, out string name)
    {
        hash = name = string.Empty;
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
        {
            return false;
        }

        var bsd = BsdLine().Match(trimmed);
        if (bsd.Success)
        {
            name = bsd.Groups["name"].Value;
            hash = bsd.Groups["hash"].Value;
            return true;
        }

        var gnu = GnuLine().Match(trimmed);
        if (gnu.Success)
        {
            name = gnu.Groups["name"].Value;
            hash = gnu.Groups["hash"].Value;
            return true;
        }

        var sfv = SfvLine().Match(trimmed);
        if (sfv.Success)
        {
            name = sfv.Groups["name"].Value;
            hash = sfv.Groups["hash"].Value;
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"^(?:SHA256|SHA512|SHA-256|SHA-512|CRC32)\s*\((?<name>.+)\)\s*=\s*(?<hash>[0-9A-Fa-f]+)$")]
    private static partial Regex BsdLine();

    [GeneratedRegex(@"^(?<hash>[0-9A-Fa-f]{8}|[0-9A-Fa-f]{64}|[0-9A-Fa-f]{128})\s+\*?(?<name>.+)$")]
    private static partial Regex GnuLine();

    /// <summary>Simple File Verification: "name CRC32".</summary>
    [GeneratedRegex(@"^(?<name>.+?)\s+(?<hash>[0-9A-Fa-f]{8})$")]
    private static partial Regex SfvLine();
}
