using System.Runtime.InteropServices;
using PackWise.Archiving.SevenZip;
using PackWise.Archiving.SevenZip.Interop;

namespace PackWise.Archiving;

/// <summary>
/// A finished archive written to a temporary sibling file. Nothing at the target path
/// changes until <see cref="Commit"/> is called.
/// </summary>
public sealed class PendingArchive : IDisposable
{
    private bool _settled;

    internal PendingArchive(string temporaryPath, string targetPath)
    {
        TemporaryPath = temporaryPath;
        TargetPath = targetPath;
    }

    public string TemporaryPath { get; }

    public string TargetPath { get; }

    /// <summary>Moves the new archive into place, replacing any existing file at the target.</summary>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_settled, this);
        if (File.Exists(TargetPath))
        {
            var backup = TargetPath + ".pwbak";
            File.Replace(TemporaryPath, TargetPath, backup, ignoreMetadataErrors: true);
            File.Delete(backup);
        }
        else
        {
            File.Move(TemporaryPath, TargetPath);
        }

        _settled = true;
    }

    public void Dispose()
    {
        if (!_settled)
        {
            _settled = true;
            TryDelete(TemporaryPath);
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public static class ArchiveWriter
{
    /// <summary>Formats PackWise offers for new archives, in display order.</summary>
    public static IReadOnlyList<ArchiveFormat> CreatableFormats =>
        ArchiveReader.Formats.Where(f => f.CanCreate).OrderBy(f => f.Name == "zip" ? 0 : 1).ToList();

    public static ArchiveFormat FindFormat(string name) =>
        ArchiveReader.Formats.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArchiveException($"The {name} format is not available in this engine build.");

    /// <summary>Writes a new archive. The result must be committed to appear at <paramref name="outputPath"/>.</summary>
    public static PendingArchive Create(
        string outputPath,
        IReadOnlyList<ArchiveSourceItem> items,
        CreateArchiveOptions options,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);
        EnsureEngineThread();

        if (!options.Format.CanCreate)
        {
            throw new ArchiveException($"PackWise does not create {options.Format.DisplayName} archives.");
        }

        var outArchive = SevenZipLibrary.Instance.CreateObject<IOutArchive>(options.Format);
        try
        {
            ApplyProperties(outArchive, options);
            var callback = new UpdateCallback([], items, options.Password, progress, cancellationToken);
            return WriteTo(outputPath, outArchive, callback, cancellationToken);
        }
        finally
        {
            Marshal.FinalReleaseComObject(outArchive);
        }
    }

    /// <summary>
    /// Produces a copy of an open archive with entries removed and/or new items added.
    /// New items replace existing entries with the same path. Dispose the reader before committing.
    /// </summary>
    public static PendingArchive Rewrite(
        ArchiveReader reader,
        IReadOnlyCollection<int> removeIndices,
        IReadOnlyList<ArchiveSourceItem> addItems,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!reader.Info.Format.CanUpdate || reader.Info.FormatChain.Count > 1 || reader.Info.IsMultiVolume)
        {
            throw new ArchiveException($"PackWise cannot modify {reader.Info.Format.DisplayName} archives in place.");
        }

        var removed = new HashSet<int>(removeIndices);
        var replaced = new HashSet<string>(addItems.Select(i => i.ArchivePath.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
        var kept = reader.Entries
            .Where(e => !removed.Contains(e.Index) && !replaced.Contains(e.Path.TrimEnd('\\')))
            .Select(e => e.Index)
            .ToList();

        return reader.WithHandle(handle =>
        {
            // Same COM identity as the reader's handle; released with the reader.
            var outArchive = (IOutArchive)handle;
            var callback = new UpdateCallback(kept, addItems, reader.Password, progress, cancellationToken);
            return WriteTo(reader.Info.FilePath, outArchive, callback, cancellationToken);
        });
    }

    private static PendingArchive WriteTo(string targetPath, IOutArchive outArchive, UpdateCallback callback, CancellationToken cancellationToken)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var temporary = Path.Combine(Path.GetDirectoryName(fullTarget)!, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.pwtmp");
        var pending = new PendingArchive(temporary, fullTarget);

        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16))
            using (var output = new OutStreamAdapter(file, ownsStream: false))
            {
                int hr;
                try
                {
                    hr = outArchive.UpdateItems(output, callback.ItemCount, callback);
                }
                finally
                {
                    callback.CloseStreams();
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (callback.FailedPath is not null)
                {
                    throw new ArchiveException($"Could not read “{callback.FailedPath}”: {callback.Error?.Message}", callback.Error!);
                }

                if (callback.Error is not null)
                {
                    throw new ArchiveException($"Could not read a source file: {callback.Error.Message}", callback.Error);
                }

                if (output.Error is not null)
                {
                    throw new ArchiveException($"Could not write the archive: {output.Error.Message}", output.Error);
                }

                if (hr != HResult.Ok)
                {
                    throw new ArchiveException($"The archive engine could not write the archive (0x{hr:X8}).");
                }

                file.Flush(flushToDisk: true);
            }

            return pending;
        }
        catch
        {
            pending.Dispose();
            throw;
        }
    }

    private static void ApplyProperties(IOutArchive outArchive, CreateArchiveOptions options)
    {
        if (outArchive is not ISetProperties setter)
        {
            return;
        }

        var hasPassword = !string.IsNullOrEmpty(options.Password);
        var properties = new List<(string Name, PropVariant Value)>
        {
            ("x", PropVariant.FromUInt32((uint)options.Level)),
        };

        if (options.ThreadCount is { } threads)
        {
            properties.Add(("mt", PropVariant.FromUInt32((uint)Math.Max(1, threads))));
        }

        if (options.Format.Name.Equals("zip", StringComparison.OrdinalIgnoreCase))
        {
            properties.Add(("cu", PropVariant.FromBool(true)));
            if (hasPassword)
            {
                properties.Add(("em", PropVariant.FromString("AES256")));
            }
        }
        else if (options.Format.Name.Equals("7z", StringComparison.OrdinalIgnoreCase))
        {
            properties.Add(("s", PropVariant.FromBool(options.Solid)));
            if (hasPassword && options.EncryptFileNames)
            {
                properties.Add(("he", PropVariant.FromBool(true)));
            }
        }

        SetProperties(setter, properties);
    }

    private static unsafe void SetProperties(ISetProperties setter, List<(string Name, PropVariant Value)> properties)
    {
        var names = new IntPtr[properties.Count];
        var values = new PropVariant[properties.Count];
        try
        {
            for (var i = 0; i < properties.Count; i++)
            {
                names[i] = Marshal.StringToHGlobalUni(properties[i].Name);
                values[i] = properties[i].Value;
            }

            fixed (IntPtr* namesPointer = names)
            fixed (PropVariant* valuesPointer = values)
            {
                var hr = setter.SetProperties((IntPtr)namesPointer, (IntPtr)valuesPointer, (uint)properties.Count);
                if (hr != HResult.Ok)
                {
                    throw new ArchiveException($"The archive engine rejected the compression settings (0x{hr:X8}).");
                }
            }
        }
        finally
        {
            for (var i = 0; i < properties.Count; i++)
            {
                Marshal.FreeHGlobal(names[i]);
                values[i].Clear();
            }
        }
    }

    private static void EnsureEngineThread()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            throw new InvalidOperationException("Archive engine calls must run on a background thread.");
        }
    }
}
