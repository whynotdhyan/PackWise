namespace PackWise.Archiving.SevenZip.Interop;

/// <summary>Exposes a seekable .NET stream to the engine as IInStream.</summary>
internal sealed class InStreamAdapter : IInStream, ISequentialInStream, IStreamGetSize, IDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;

    public InStreamAdapter(Stream stream, bool ownsStream = true)
    {
        _stream = stream;
        _ownsStream = ownsStream;
    }

    public Exception? Error { get; private set; }

    public unsafe int Read(IntPtr data, uint size, IntPtr processedSize)
    {
        try
        {
            var read = _stream.Read(new Span<byte>((void*)data, (int)Math.Min(size, int.MaxValue)));
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
        try
        {
            var position = _stream.Seek(offset, (SeekOrigin)seekOrigin);
            if (newPosition != IntPtr.Zero)
            {
                *(ulong*)newPosition = (ulong)position;
            }

            return HResult.Ok;
        }
        catch (Exception ex)
        {
            Error ??= ex;
            return HResult.Fail;
        }
    }

    public int GetSize(out ulong size)
    {
        size = (ulong)_stream.Length;
        return HResult.Ok;
    }

    public void Dispose()
    {
        if (_ownsStream)
        {
            _stream.Dispose();
        }
    }
}

/// <summary>Exposes a .NET stream to the engine as a sequential or seekable output.</summary>
internal sealed class OutStreamAdapter : IOutStream, ISequentialOutStream, IDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly long _byteLimit;

    public OutStreamAdapter(Stream stream, bool ownsStream = true, long byteLimit = long.MaxValue)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _byteLimit = byteLimit;
    }

    public long BytesWritten { get; private set; }

    public bool LimitExceeded { get; private set; }

    public Exception? Error { get; private set; }

    public unsafe int Write(IntPtr data, uint size, IntPtr processedSize)
    {
        try
        {
            if (BytesWritten + size > _byteLimit)
            {
                LimitExceeded = true;
                return HResult.Abort;
            }

            _stream.Write(new ReadOnlySpan<byte>((void*)data, (int)size));
            BytesWritten += size;
            if (processedSize != IntPtr.Zero)
            {
                *(uint*)processedSize = size;
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
        try
        {
            var position = _stream.Seek(offset, (SeekOrigin)seekOrigin);
            if (newPosition != IntPtr.Zero)
            {
                *(ulong*)newPosition = (ulong)position;
            }

            return HResult.Ok;
        }
        catch (Exception ex)
        {
            Error ??= ex;
            return HResult.Fail;
        }
    }

    public int SetSize(ulong newSize)
    {
        try
        {
            _stream.SetLength((long)newSize);
            return HResult.Ok;
        }
        catch (Exception ex)
        {
            Error ??= ex;
            return HResult.Fail;
        }
    }

    public void Dispose()
    {
        if (_ownsStream)
        {
            _stream.Dispose();
        }
    }
}
