using System.Text;
using PackWise.Archiving;

namespace PackWise.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "packwise-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    /// <summary>Creates files under a fresh folder and returns that folder.</summary>
    public string CreateTree(Dictionary<string, string> files)
    {
        var folder = Path("src-" + Guid.NewGuid().ToString("N")[..8]);
        foreach (var (relative, content) in files)
        {
            var full = System.IO.Path.Combine(folder, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        return folder;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class MemoryTarget : IExtractTarget
{
    private readonly Dictionary<string, MemoryStream> _files = new(StringComparer.OrdinalIgnoreCase);

    public Stream? OpenEntry(ArchiveEntry entry)
    {
        var stream = new MemoryStream();
        _files[entry.Path] = stream;
        return stream;
    }

    public long GetByteLimit(ArchiveEntry entry) => long.MaxValue;

    public void CompleteEntry(ArchiveEntry entry, Stream stream, EntryResult result, long bytesWritten)
    {
    }

    public string Text(string path) => Encoding.UTF8.GetString(_files[path].ToArray());
}

internal sealed class SequencePasswordProvider : IPasswordProvider
{
    private readonly Queue<string> _passwords;

    public SequencePasswordProvider(params string[] passwords) => _passwords = new Queue<string>(passwords);

    public int Calls { get; private set; }

    public string? GetPassword(string archivePath, bool previousAttemptFailed)
    {
        Calls++;
        return _passwords.Count > 0 ? _passwords.Dequeue() : null;
    }
}
