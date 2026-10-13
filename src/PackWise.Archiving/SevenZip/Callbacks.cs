using PackWise.Archiving.SevenZip.Interop;

namespace PackWise.Archiving.SevenZip;

/// <summary>Holds the password state shared across open and extract callbacks of one archive.</summary>
internal sealed class PasswordState
{
    private readonly string _archivePath;
    private readonly IPasswordProvider? _provider;
    private bool _failedOnce;

    public PasswordState(string archivePath, string? initialPassword, IPasswordProvider? provider)
    {
        _archivePath = archivePath;
        _provider = provider;
        Password = initialPassword;
    }

    public string? Password { get; private set; }

    public bool WasRequested { get; private set; }

    public bool WasCanceled { get; private set; }

    /// <summary>Clears per-operation request flags; the remembered password is kept.</summary>
    public void BeginOperation()
    {
        WasRequested = false;
        WasCanceled = false;
    }

    public void MarkFailed()
    {
        _failedOnce = true;
        Password = null;
    }

    public int Supply(out string? password)
    {
        WasRequested = true;
        if (Password is null && _provider is not null)
        {
            Password = _provider.GetPassword(_archivePath, _failedOnce);
            WasCanceled = Password is null;
        }

        password = Password;
        return password is null ? HResult.Abort : HResult.Ok;
    }
}

internal sealed class OpenCallback : IArchiveOpenCallback, IArchiveOpenVolumeCallback, ICryptoGetTextPassword
{
    private readonly string _firstVolumePath;
    private readonly PasswordState _password;
    private readonly CancellationToken _cancellationToken;
    private readonly List<IDisposable> _resources;
    private string _currentVolumeName;

    public OpenCallback(string firstVolumePath, PasswordState password, List<IDisposable> resources, CancellationToken cancellationToken)
    {
        _firstVolumePath = firstVolumePath;
        _currentVolumeName = Path.GetFileName(firstVolumePath);
        _password = password;
        _resources = resources;
        _cancellationToken = cancellationToken;
    }

    public int VolumeCount { get; private set; } = 1;

    public int SetTotal(IntPtr files, IntPtr bytes) => HResult.Ok;

    public int SetCompleted(IntPtr files, IntPtr bytes) =>
        _cancellationToken.IsCancellationRequested ? HResult.Abort : HResult.Ok;

    public int CryptoGetTextPassword(out string? password) => _password.Supply(out password);

    public int GetProperty(uint propId, IntPtr value)
    {
        if (propId == ItemProperty.Name)
        {
            PropVariant.Write(value, PropVariant.FromString(_currentVolumeName));
        }
        else
        {
            PropVariant.Write(value, default);
        }

        return HResult.Ok;
    }

    public int GetStream(string name, out IInStream? inStream)
    {
        inStream = null;

        // Volume names come from the archive; only accept plain siblings of the first volume.
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(['\\', '/', ':']) >= 0 || name is "." or "..")
        {
            return HResult.False;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(_firstVolumePath))!;
        var candidate = Path.Combine(directory, name);
        if (!File.Exists(candidate))
        {
            return HResult.False;
        }

        try
        {
            var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
            var adapter = new InStreamAdapter(stream);
            _resources.Add(adapter);
            VolumeCount++;
            _currentVolumeName = name;
            inStream = adapter;
            return HResult.Ok;
        }
        catch (IOException)
        {
            return HResult.False;
        }
        catch (UnauthorizedAccessException)
        {
            return HResult.False;
        }
    }
}

internal sealed class ExtractCallback : IArchiveExtractCallback, ICryptoGetTextPassword
{
    private readonly IReadOnlyList<ArchiveEntry> _entries;
    private readonly IExtractTarget? _target;
    private readonly PasswordState _password;
    private readonly IProgress<ArchiveProgress>? _progress;
    private readonly CancellationToken _cancellationToken;
    private readonly List<EntryOutcome> _outcomes = [];

    private ArchiveEntry? _current;
    private Stream? _currentStream;
    private OutStreamAdapter? _currentAdapter;
    private ulong _total;

    public ExtractCallback(
        IReadOnlyList<ArchiveEntry> entries,
        IExtractTarget? target,
        PasswordState password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        _entries = entries;
        _target = target;
        _password = password;
        _progress = progress;
        _cancellationToken = cancellationToken;
    }

    public IReadOnlyList<EntryOutcome> Outcomes => _outcomes;

    public Exception? TargetError { get; private set; }

    public bool LimitExceeded { get; private set; }

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
            _progress?.Report(new ArchiveProgress(*(ulong*)completeValue, _total, _current?.Path));
        }

        return HResult.Ok;
    }

    public int GetStream(uint index, out ISequentialOutStream? outStream, int askExtractMode)
    {
        outStream = null;
        _current = index < _entries.Count ? _entries[(int)index] : null;

        if (_cancellationToken.IsCancellationRequested)
        {
            return HResult.Abort;
        }

        if (askExtractMode != AskMode.Extract || _target is null || _current is null || _current.IsDirectory)
        {
            return HResult.Ok;
        }

        try
        {
            _currentStream = _target.OpenEntry(_current);
            if (_currentStream is not null)
            {
                _currentAdapter = new OutStreamAdapter(_currentStream, ownsStream: false, byteLimit: _target.GetByteLimit(_current));
                outStream = _currentAdapter;
            }

            return HResult.Ok;
        }
        catch (Exception ex)
        {
            TargetError ??= ex;
            return HResult.Abort;
        }
    }

    public int PrepareOperation(int askExtractMode) => HResult.Ok;

    public int SetOperationResult(int operationResult)
    {
        if (_current is null)
        {
            return HResult.Ok;
        }

        var result = Map(operationResult);
        if (_currentAdapter is { } adapter)
        {
            if (adapter.LimitExceeded)
            {
                result = EntryResult.SizeLimitExceeded;
                LimitExceeded = true;
            }
            else if (adapter.Error is not null)
            {
                result = EntryResult.WriteFailed;
                TargetError ??= adapter.Error;
            }
        }

        if (result is EntryResult.WrongPassword or EntryResult.DataError or EntryResult.CrcError && _current.IsEncrypted)
        {
            _password.MarkFailed();
        }

        CompleteCurrent(result);
        _outcomes.Add(new EntryOutcome(_current, result));
        _current = null;
        return LimitExceeded ? HResult.Abort : HResult.Ok;
    }

    public int CryptoGetTextPassword(out string? password) => _password.Supply(out password);

    /// <summary>Finalises an entry left open when the engine aborted mid-item.</summary>
    public void Abandon(EntryResult result)
    {
        if (_currentAdapter is { LimitExceeded: true })
        {
            LimitExceeded = true;
            result = EntryResult.SizeLimitExceeded;
        }

        if (_current is not null && _currentStream is not null)
        {
            CompleteCurrent(result);
            _outcomes.Add(new EntryOutcome(_current, result));
        }
    }

    private void CompleteCurrent(EntryResult result)
    {
        if (_currentStream is not null && _target is not null && _current is not null)
        {
            try
            {
                _target.CompleteEntry(_current, _currentStream, result, _currentAdapter?.BytesWritten ?? 0);
            }
            catch (Exception ex)
            {
                TargetError ??= ex;
            }
        }

        _currentStream = null;
        _currentAdapter = null;
    }

    private static EntryResult Map(int code) => code switch
    {
        OperationResultCode.Ok => EntryResult.Ok,
        OperationResultCode.UnsupportedMethod => EntryResult.UnsupportedMethod,
        OperationResultCode.DataError => EntryResult.DataError,
        OperationResultCode.CrcError => EntryResult.CrcError,
        OperationResultCode.Unavailable => EntryResult.Unavailable,
        OperationResultCode.UnexpectedEnd => EntryResult.UnexpectedEnd,
        OperationResultCode.DataAfterEnd => EntryResult.DataAfterEnd,
        OperationResultCode.IsNotArc => EntryResult.NotArchive,
        OperationResultCode.HeadersError => EntryResult.HeadersError,
        OperationResultCode.WrongPassword => EntryResult.WrongPassword,
        _ => EntryResult.DataError,
    };
}
