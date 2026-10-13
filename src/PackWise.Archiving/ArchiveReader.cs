using System.Runtime.InteropServices;
using PackWise.Archiving.SevenZip;
using PackWise.Archiving.SevenZip.Interop;

namespace PackWise.Archiving;

/// <summary>Outcome of an extract or test run over an open archive.</summary>
public sealed class ArchiveRunResult
{
    public required IReadOnlyList<EntryOutcome> Outcomes { get; init; }

    public bool Canceled { get; init; }

    public bool PasswordCanceled { get; init; }

    public bool LimitExceeded { get; init; }

    /// <summary>A failure raised by the output target or the engine that stopped the run.</summary>
    public Exception? Error { get; init; }

    public bool Succeeded => !Canceled && !PasswordCanceled && !LimitExceeded && Error is null && Outcomes.All(o => o.Result.IsSuccess());
}

/// <summary>
/// An open archive. Listing data is plain managed state and may be read from any thread;
/// engine operations must run on a background (MTA) thread and are serialised per instance.
/// </summary>
public sealed class ArchiveReader : IDisposable
{
    private const int HeaderProbeSize = 64 * 1024;
    private const int MaxPasswordAttempts = 10;
    private const ulong MaxSignatureSearch = 1 << 22;

    private static readonly string[] TarAliases = ["tgz", "tbz", "tbz2", "txz", "tzst", "taz"];

    private readonly Lock _sync = new();
    private readonly List<IInArchive> _handles;
    private readonly List<IDisposable> _resources;
    private readonly PasswordState _password;
    private readonly IInArchive _archive;
    private bool _disposed;

    private ArchiveReader(
        IInArchive archive,
        List<IInArchive> handles,
        List<IDisposable> resources,
        PasswordState password,
        ArchiveInfo info,
        IReadOnlyList<ArchiveEntry> entries)
    {
        _archive = archive;
        _handles = handles;
        _resources = resources;
        _password = password;
        Info = info;
        Entries = entries;
    }

    public ArchiveInfo Info { get; }

    public IReadOnlyList<ArchiveEntry> Entries { get; }

    /// <summary>The password that successfully opened or was last supplied for this archive (memory only).</summary>
    public string? Password => _password.Password;

    public static IReadOnlyList<ArchiveFormat> Formats => SevenZipLibrary.Instance.Formats;

    public static string EngineVersion => SevenZipLibrary.Instance.Version;

    /// <summary>
    /// Identifies the format of a file from its name and leading bytes without opening it.
    /// </summary>
    public static ArchiveFormat? Detect(string path, bool allowUntrustedFormats = false)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var header = ReadHeader(stream);
            return RankCandidates(path, header, allowUntrustedFormats, out _).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Returns true when the name alone suggests a format PackWise opens by default.</summary>
    public static bool LooksLikeArchive(string path)
    {
        var extension = ExtensionOf(path);
        return extension.Length > 0 && Formats.Any(f => f.IsTrusted && f.MatchesExtension(extension));
    }

    public static ArchiveReader Open(string path, ArchiveOpenOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= ArchiveOpenOptions.Default;
        EnsureEngineThread();

        var fullPath = Path.GetFullPath(path);
        var password = new PasswordState(fullPath, options.Password, options.PasswordProvider);
        var resources = new List<IDisposable>();
        var handles = new List<IInArchive>();

        try
        {
            var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
            var stream = new InStreamAdapter(fileStream);
            resources.Add(stream);

            var header = ReadHeader(fileStream);
            var candidates = RankCandidates(fullPath, header, options.AllowUntrustedFormats, out var disabledMatch);
            if (candidates.Count == 0)
            {
                throw new ArchiveNotRecognizedException(disabledMatch is null
                    ? $"“{Path.GetFileName(fullPath)}” is not an archive PackWise can open."
                    : $"“{Path.GetFileName(fullPath)}” looks like a {disabledMatch.DisplayName} file. Opening this format is turned off in Settings › Formats.")
                {
                    DisabledFormatName = disabledMatch?.Name,
                };
            }

            var callback = new OpenCallback(fullPath, password, resources, cancellationToken);
            var (archive, format) = OpenFirstMatching(candidates, stream, fileStream, callback, password, fullPath, cancellationToken);
            handles.Add(archive);

            var chain = new List<ArchiveFormat> { format };
            var volumeCount = callback.VolumeCount;
            var outerArchive = archive;

            if (format.Name.Equals("Split", StringComparison.OrdinalIgnoreCase) &&
                TryOpenInner(archive, options, password, resources, cancellationToken) is { } inner)
            {
                handles.Add(inner.Archive);
                chain.Add(inner.Format);
                archive = inner.Archive;
                format = inner.Format;
            }

            var info = ReadInfo(archive, outerArchive, fullPath, format, chain, volumeCount, password.WasRequested);
            var entries = ReadEntries(archive, fullPath, format, cancellationToken);
            return new ArchiveReader(archive, handles, resources, password, info, entries);
        }
        catch
        {
            ReleaseAll(handles, resources);
            throw;
        }
    }

    public ArchiveRunResult Extract(IReadOnlyCollection<int>? indices, IExtractTarget target, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Run(indices, target, testMode: false, progress, cancellationToken);
    }

    public ArchiveRunResult Test(IReadOnlyCollection<int>? indices = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Run(indices, target: null, testMode: true, progress, cancellationToken);

    internal T WithHandle<T>(Func<IInArchive, T> action)
    {
        EnsureEngineThread();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return action(_archive);
        }
    }

    public void Dispose()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            Task.Run(Dispose).GetAwaiter().GetResult();
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseAll(_handles, _resources);
        }
    }

    private ArchiveRunResult Run(IReadOnlyCollection<int>? indices, IExtractTarget? target, bool testMode, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        EnsureEngineThread();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var sorted = indices?.Where(i => i >= 0 && i < Entries.Count).Select(i => (uint)i).Distinct().Order().ToArray();
            if (sorted is { Length: 0 })
            {
                return new ArchiveRunResult { Outcomes = [] };
            }

            _password.BeginOperation();
            var callback = new ExtractCallback(Entries, target, _password, progress, cancellationToken);
            int hr;
            try
            {
                hr = _archive.Extract(sorted, sorted is null ? uint.MaxValue : (uint)sorted.Length, testMode ? 1 : 0, callback);
            }
            catch (Exception ex)
            {
                callback.Abandon(EntryResult.WriteFailed);
                return new ArchiveRunResult { Outcomes = callback.Outcomes, Error = ex };
            }

            var canceled = cancellationToken.IsCancellationRequested;
            var passwordCanceled = _password.WasCanceled;
            if (hr != HResult.Ok)
            {
                callback.Abandon(callback.LimitExceeded ? EntryResult.SizeLimitExceeded : EntryResult.Canceled);
            }

            Exception? error = callback.TargetError;
            if (hr != HResult.Ok && hr != HResult.Abort && error is null)
            {
                error = new ArchiveException($"The archive engine reported an error (0x{hr:X8}).");
            }

            return new ArchiveRunResult
            {
                Outcomes = callback.Outcomes,
                Canceled = canceled,
                PasswordCanceled = passwordCanceled && !canceled,
                LimitExceeded = callback.LimitExceeded,
                Error = error,
            };
        }
    }

    private static (IInArchive Archive, ArchiveFormat Format) OpenFirstMatching(
        IReadOnlyList<ArchiveFormat> candidates,
        IInStream stream,
        Stream rawStream,
        OpenCallback callback,
        PasswordState password,
        string path,
        CancellationToken cancellationToken)
    {
        foreach (var format in candidates)
        {
            for (var attempt = 0; attempt < MaxPasswordAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rawStream.Position = 0;

                var archive = SevenZipLibrary.Instance.CreateObject<IInArchive>(format);
                var hr = OpenHandle(archive, stream, callback);
                if (hr == HResult.Ok)
                {
                    return (archive, format);
                }

                ReleaseHandle(archive);
                cancellationToken.ThrowIfCancellationRequested();

                if (password.WasCanceled)
                {
                    throw new ArchivePasswordException($"“{Path.GetFileName(path)}” is encrypted and needs a password to open.");
                }

                if (!password.WasRequested)
                {
                    break;
                }

                // The handler asked for a password and still failed: the password was wrong.
                password.MarkFailed();
            }

            if (password.WasRequested)
            {
                throw new ArchivePasswordException($"Could not open “{Path.GetFileName(path)}”. The password may be wrong.");
            }
        }

        throw new ArchiveNotRecognizedException($"“{Path.GetFileName(path)}” could not be opened. It may be damaged or use an unsupported format.");
    }

    private static unsafe int OpenHandle(IInArchive archive, IInStream stream, IArchiveOpenCallback callback)
    {
        var maxCheck = MaxSignatureSearch;
        return archive.Open(stream, (IntPtr)(&maxCheck), callback);
    }

    private static (IInArchive Archive, ArchiveFormat Format)? TryOpenInner(
        IInArchive outer,
        ArchiveOpenOptions options,
        PasswordState password,
        List<IDisposable> resources,
        CancellationToken cancellationToken)
    {
        if (outer is not IInArchiveGetStream getStream)
        {
            return null;
        }

        var mainIndex = ReadArchiveValue(outer, ItemProperty.MainSubfile).AsUInt32Cleared() ?? 0;
        var name = ReadItemValue(outer, mainIndex, ItemProperty.Path).AsStringCleared() ?? string.Empty;

        if (getStream.GetStream(mainIndex, out var sequential) != HResult.Ok || sequential is not IInStream inner)
        {
            return null;
        }

        var header = ReadHeader(inner);
        var callback = new OpenCallback(name, password, resources, cancellationToken);
        foreach (var format in RankCandidates(name, header, options.AllowUntrustedFormats, out _))
        {
            if (format.Name.Equals("Split", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SeekStart(inner);
            var archive = SevenZipLibrary.Instance.CreateObject<IInArchive>(format);
            if (OpenHandle(archive, inner, callback) == HResult.Ok)
            {
                return (archive, format);
            }

            ReleaseHandle(archive);
        }

        return null;
    }

    private static List<ArchiveFormat> RankCandidates(string path, ReadOnlySpan<byte> header, bool allowUntrusted, out ArchiveFormat? disabledMatch)
    {
        var extension = ExtensionOf(path);
        var both = new List<ArchiveFormat>();
        var signatureOnly = new List<ArchiveFormat>();
        var extensionOnly = new List<ArchiveFormat>();
        disabledMatch = null;

        foreach (var format in Formats)
        {
            var byExtension = extension.Length > 0 && format.MatchesExtension(extension);
            var bySignature = format.MatchesSignature(header);
            if (!byExtension && !bySignature)
            {
                continue;
            }

            if (!format.IsTrusted && !allowUntrusted)
            {
                disabledMatch ??= format;
                continue;
            }

            (byExtension && bySignature ? both : bySignature ? signatureOnly : extensionOnly).Add(format);
        }

        return [.. both, .. signatureOnly, .. extensionOnly];
    }

    private static ArchiveInfo ReadInfo(IInArchive archive, IInArchive outer, string path, ArchiveFormat format, IReadOnlyList<ArchiveFormat> chain, int volumeCount, bool passwordRequestedOnOpen)
    {
        var errors = (ArchiveIssues)(ReadArchiveValue(archive, ItemProperty.ErrorFlags).AsUInt32Cleared() ?? 0);
        var warnings = (ArchiveIssues)(ReadArchiveValue(archive, ItemProperty.WarningFlags).AsUInt32Cleared() ?? 0);
        var reportedVolumes = (int)(ReadArchiveValue(outer, ItemProperty.NumVolumes).AsUInt32Cleared() ?? 0);

        return new ArchiveInfo
        {
            FilePath = path,
            Format = format,
            FormatChain = chain,
            PhysicalSize = ReadArchiveValue(archive, ItemProperty.PhySize).AsUInt64Cleared(),
            HeadersSize = ReadArchiveValue(archive, ItemProperty.HeadersSize).AsUInt64Cleared(),
            IsSolid = ReadArchiveValue(archive, ItemProperty.Solid).AsBoolCleared() ?? false,
            VolumeCount = Math.Max(volumeCount, reportedVolumes),
            Comment = ReadArchiveValue(archive, ItemProperty.Comment).AsStringCleared(),
            Method = ReadArchiveValue(archive, ItemProperty.Method).AsStringCleared(),
            Offset = ReadArchiveValue(archive, ItemProperty.Offset).AsUInt64Cleared(),
            HasEncryptedHeaders = passwordRequestedOnOpen,
            Errors = errors,
            Warnings = warnings,
            ErrorMessage = ReadArchiveValue(archive, ItemProperty.Error).AsStringCleared(),
        };
    }

    private static List<ArchiveEntry> ReadEntries(IInArchive archive, string archivePath, ArchiveFormat format, CancellationToken cancellationToken)
    {
        if (archive.GetNumberOfItems(out var count) != HResult.Ok)
        {
            throw new ArchiveException("The archive listing could not be read.");
        }

        var entries = new List<ArchiveEntry>((int)Math.Min(count, 1_000_000));
        for (uint i = 0; i < count; i++)
        {
            if ((i & 0x3FF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var path = ReadItemValue(archive, i, ItemProperty.Path).AsStringCleared();
            if (string.IsNullOrEmpty(path))
            {
                path = count == 1 ? DefaultItemName(archivePath, format) : $"[item {i + 1}]";
            }

            var attributes = ReadItemValue(archive, i, ItemProperty.Attrib).AsUInt32Cleared();
            var symLink = ReadItemValue(archive, i, ItemProperty.SymLink).AsStringCleared();
            var hardLink = ReadItemValue(archive, i, ItemProperty.HardLink).AsStringCleared();
            var posixMode = ReadItemValue(archive, i, ItemProperty.PosixAttrib).AsUInt32Cleared() ?? PosixModeFromAttributes(attributes);
            var isDirectory = ReadItemValue(archive, i, ItemProperty.IsDir).AsBoolCleared() ?? false;

            entries.Add(new ArchiveEntry
            {
                Index = (int)i,
                Path = path.Replace('/', '\\'),
                IsDirectory = isDirectory,
                Size = ReadItemValue(archive, i, ItemProperty.Size).AsUInt64Cleared(),
                PackedSize = ReadItemValue(archive, i, ItemProperty.PackSize).AsUInt64Cleared(),
                Modified = ReadItemValue(archive, i, ItemProperty.MTime).AsDateTimeCleared(),
                Created = ReadItemValue(archive, i, ItemProperty.CTime).AsDateTimeCleared(),
                Attributes = attributes,
                Crc = ReadItemValue(archive, i, ItemProperty.Crc).AsUInt32Cleared(),
                IsEncrypted = ReadItemValue(archive, i, ItemProperty.Encrypted).AsBoolCleared() ?? false,
                Method = ReadItemValue(archive, i, ItemProperty.Method).AsStringCleared(),
                Comment = ReadItemValue(archive, i, ItemProperty.Comment).AsStringCleared(),
                LinkTarget = string.IsNullOrEmpty(symLink) ? hardLink : symLink,
                IsLink = !string.IsNullOrEmpty(symLink) || !string.IsNullOrEmpty(hardLink) ||
                         (attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                         (posixMode is { } mode && (mode & 0xF000) == 0xA000),
                IsAlternateStream = ReadItemValue(archive, i, ItemProperty.IsAltStream).AsBoolCleared() ?? false,
            });
        }

        return entries;
    }

    private static uint? PosixModeFromAttributes(uint? attributes) =>
        attributes is { } value && (value & 0x8000) != 0 ? value >> 16 : null;

    private static string DefaultItemName(string archivePath, ArchiveFormat format)
    {
        var extension = ExtensionOf(archivePath);
        var stem = Path.GetFileNameWithoutExtension(archivePath);
        if (TarAliases.Contains(extension, StringComparer.Ordinal))
        {
            return stem + ".tar";
        }

        return format.Category == FormatCategory.CompressedStream && stem.Length > 0 ? stem : Path.GetFileName(archivePath);
    }

    private static string ExtensionOf(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    private static byte[] ReadHeader(Stream stream)
    {
        var buffer = new byte[HeaderProbeSize];
        stream.Position = 0;
        var total = 0;
        int read;
        while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
        }

        stream.Position = 0;
        return buffer[..total];
    }

    private static unsafe byte[] ReadHeader(IInStream stream)
    {
        var buffer = new byte[HeaderProbeSize];
        var total = 0;
        fixed (byte* pointer = buffer)
        {
            while (total < buffer.Length)
            {
                uint read = 0;
                if (stream.Read((IntPtr)(pointer + total), (uint)(buffer.Length - total), (IntPtr)(&read)) != HResult.Ok || read == 0)
                {
                    break;
                }

                total += (int)read;
            }
        }

        SeekStart(stream);
        return buffer[..total];
    }

    private static void SeekStart(IInStream stream) => stream.Seek(0, 0, IntPtr.Zero);

    internal static unsafe PropVariant ReadItemValue(IInArchive archive, uint index, uint propId)
    {
        PropVariant value = default;
        if (archive.GetProperty(index, propId, (IntPtr)(&value)) != HResult.Ok)
        {
            value.Clear();
        }

        return value;
    }

    private static unsafe PropVariant ReadArchiveValue(IInArchive archive, uint propId)
    {
        PropVariant value = default;
        if (archive.GetArchiveProperty(propId, (IntPtr)(&value)) != HResult.Ok)
        {
            value.Clear();
        }

        return value;
    }

    private static void EnsureEngineThread()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            throw new InvalidOperationException("Archive engine calls must run on a background thread.");
        }
    }

    private static void ReleaseHandle(IInArchive archive)
    {
        try
        {
            archive.Close();
        }
        finally
        {
            Marshal.FinalReleaseComObject(archive);
        }
    }

    private static void ReleaseAll(List<IInArchive> handles, List<IDisposable> resources)
    {
        for (var i = handles.Count - 1; i >= 0; i--)
        {
            ReleaseHandle(handles[i]);
        }

        handles.Clear();
        foreach (var resource in resources)
        {
            resource.Dispose();
        }

        resources.Clear();
    }
}

internal static class PropVariantReadExtensions
{
    public static string? AsStringCleared(this PropVariant value)
    {
        try
        {
            return value.AsString();
        }
        finally
        {
            value.Clear();
        }
    }

    public static ulong? AsUInt64Cleared(this PropVariant value)
    {
        var result = value.AsUInt64();
        value.Clear();
        return result;
    }

    public static uint? AsUInt32Cleared(this PropVariant value)
    {
        var result = value.AsUInt32();
        value.Clear();
        return result;
    }

    public static bool? AsBoolCleared(this PropVariant value)
    {
        var result = value.AsBool();
        value.Clear();
        return result;
    }

    public static DateTime? AsDateTimeCleared(this PropVariant value)
    {
        var result = value.AsDateTime();
        value.Clear();
        return result;
    }
}
