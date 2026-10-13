using PackWise.Archiving.SevenZip.Interop;

namespace PackWise.Archiving.SevenZip;

/// <summary>
/// Feeds the engine a new item list: existing entries copied by index, followed by new items from disk.
/// </summary>
internal sealed class UpdateCallback : IArchiveUpdateCallback, ICryptoGetTextPassword2
{
    private readonly IReadOnlyList<int> _kept;
    private readonly IReadOnlyList<ArchiveSourceItem> _added;
    private readonly string? _password;
    private readonly IProgress<ArchiveProgress>? _progress;
    private readonly CancellationToken _cancellationToken;
    private readonly Lock _sync = new();
    private readonly List<FileSourceStream> _openStreams = [];
    private ulong _total;
    private string? _currentItem;

    public UpdateCallback(
        IReadOnlyList<int> kept,
        IReadOnlyList<ArchiveSourceItem> added,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        _kept = kept;
        _added = added;
        _password = string.IsNullOrEmpty(password) ? null : password;
        _progress = progress;
        _cancellationToken = cancellationToken;
    }

    public uint ItemCount => (uint)(_kept.Count + _added.Count);

    public Exception? Error { get; private set; }

    public string? FailedPath { get; private set; }

    public int SetTotal(ulong total)
    {
        _total = total;
        return HResult.Ok;
    }

    public unsafe int SetCompleted(IntPtr completeValue)
    {
        if (_cancellationToken.IsCancellationRequested)
        {
            return HResult.Abort;
        }

        if (completeValue != IntPtr.Zero)
        {
            _progress?.Report(new ArchiveProgress(*(ulong*)completeValue, _total, _currentItem));
        }

        return HResult.Ok;
    }

    public int GetUpdateItemInfo(uint index, out int newData, out int newProperties, out uint indexInArchive)
    {
        if (index < _kept.Count)
        {
            newData = 0;
            newProperties = 0;
            indexInArchive = (uint)_kept[(int)index];
        }
        else
        {
            newData = 1;
            newProperties = 1;
            indexInArchive = uint.MaxValue;
        }

        return HResult.Ok;
    }

    public int GetProperty(uint index, uint propId, IntPtr value)
    {
        var item = AddedItem(index);
        var result = item is null ? default : propId switch
        {
            ItemProperty.Path => PropVariant.FromString(item.ArchivePath),
            ItemProperty.IsDir => PropVariant.FromBool(item.IsDirectory),
            ItemProperty.Size => PropVariant.FromUInt64(item.IsDirectory ? 0 : item.Size),
            ItemProperty.Attrib => PropVariant.FromUInt32((uint)item.Attributes),
            ItemProperty.MTime => PropVariant.FromDateTime(item.ModifiedUtc),
            ItemProperty.CTime => PropVariant.FromDateTime(item.CreatedUtc),
            ItemProperty.ATime => PropVariant.FromDateTime(item.AccessedUtc),
            ItemProperty.IsAnti => PropVariant.FromBool(false),
            _ => default,
        };

        PropVariant.Write(value, result);
        return HResult.Ok;
    }

    public int GetStream(uint index, out ISequentialInStream? inStream)
    {
        inStream = null;
        var item = AddedItem(index);
        if (item is null || item.IsDirectory)
        {
            return HResult.Ok;
        }

        if (_cancellationToken.IsCancellationRequested)
        {
            return HResult.Abort;
        }

        try
        {
            var adapter = new FileSourceStream(item.SourcePath);
            lock (_sync)
            {
                _openStreams.Add(adapter);
            }

            _currentItem = item.ArchivePath;
            inStream = adapter;
            return HResult.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error ??= ex;
            FailedPath ??= item.SourcePath;
            return HResult.Abort;
        }
    }

    public int SetOperationResult(int operationResult) => HResult.Ok;

    public int CryptoGetTextPassword2(out int passwordIsDefined, out string? password)
    {
        passwordIsDefined = _password is null ? 0 : 1;
        password = _password ?? string.Empty;
        return HResult.Ok;
    }

    public void CloseStreams()
    {
        lock (_sync)
        {
            foreach (var stream in _openStreams)
            {
                Error ??= stream.Error;
                stream.Dispose();
            }

            _openStreams.Clear();
        }
    }

    private ArchiveSourceItem? AddedItem(uint index) =>
        index >= _kept.Count && index - _kept.Count < _added.Count ? _added[(int)(index - _kept.Count)] : null;
}
