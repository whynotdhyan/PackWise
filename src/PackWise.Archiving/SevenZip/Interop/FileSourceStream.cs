namespace PackWise.Archiving.SevenZip.Interop;

/// <summary>
/// A seekable input over a source file that closes its handle once read to the end and
/// transparently reopens if the engine seeks back (the ZIP encoder re-reads files that do
/// not compress so it can store them instead). This keeps open handles bounded on large jobs.
/// </summary>
internal sealed class FileSourceStream : IInStream, ISequentialInStream, IStreamGetSize, IDisposable
{
    private readonly string _path;
    private readonly long _length;
    private FileStream? _stream;
    private long _position;

    public FileSourceStream(string path)
    {
        _path = path;
        _stream = Open(path);
        _length = _stream.Length;
    }

    public Exception? Error { get; private set; }

    public unsafe int Read(IntPtr data, uint size, IntPtr processedSize)
    {
        try
        {
            var read = 0;
            if (size > 0 && _position < _length)
            {
                var stream = EnsureOpen();
                read = stream.Read(new Span<byte>((void*)data, (int)Math.Min(size, int.MaxValue)));
                _position += read;
            }

            if (read == 0 || _position >= _length)
            {
                Close();
            }

            if (processedSize != IntPtr.Zero)
            {
                *(uint*)processedSize = (uint)read;
            }

            return HResult.Ok;
        }
        catch (Exception ex)
        {
            Error ??= ex;
            return HResult.Fail;
        }
    }

    public unsafe int Seek(long offset, uint seekOrigin, IntPtr newPosition)
    {
        var target = (SeekOrigin)seekOrigin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => -1,
        };

        if (target < 0)
        {
            return HResult.InvalidArgument;
        }

        _position = target;
        if (_stream is not null)
        {
            _stream.Position = target;
        }

        if (newPosition != IntPtr.Zero)
        {
            *(ulong*)newPosition = (ulong)target;
        }

        return HResult.Ok;
    }

    public int GetSize(out ulong size)
    {
        size = (ulong)_length;
        return HResult.Ok;
    }

    public void Dispose() => Close();

    private FileStream EnsureOpen()
    {
        if (_stream is null)
        {
            _stream = Open(_path);
            _stream.Position = _position;
        }

        return _stream;
    }

    private void Close()
    {
        _stream?.Dispose();
        _stream = null;
    }

    private static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
}
