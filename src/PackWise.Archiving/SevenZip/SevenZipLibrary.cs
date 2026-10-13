using System.Diagnostics;
using System.Runtime.InteropServices;
using PackWise.Archiving.SevenZip.Interop;

namespace PackWise.Archiving.SevenZip;

/// <summary>
/// Loads 7z.dll and exposes its format handlers. One instance is shared per process.
/// </summary>
internal sealed unsafe class SevenZipLibrary
{
    private static readonly Lock InstanceLock = new();
    private static SevenZipLibrary? _instance;

    private readonly delegate* unmanaged<Guid*, Guid*, IntPtr*, int> _createObject;

    private SevenZipLibrary(string path)
    {
        if (!Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException("PackWise requires a 64-bit process.");
        }

        var handle = NativeLibrary.Load(path);
        _createObject = (delegate* unmanaged<Guid*, Guid*, IntPtr*, int>)NativeLibrary.GetExport(handle, "CreateObject");
        var getNumberOfFormats = (delegate* unmanaged<uint*, int>)NativeLibrary.GetExport(handle, "GetNumberOfFormats");
        var getHandlerProperty = (delegate* unmanaged<uint, uint, PropVariant*, int>)NativeLibrary.GetExport(handle, "GetHandlerProperty2");

        Path = path;
        Version = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "unknown";
        Formats = ReadFormats(getNumberOfFormats, getHandlerProperty);
    }

    public string Path { get; }

    public string Version { get; }

    public IReadOnlyList<ArchiveFormat> Formats { get; }

    public static SevenZipLibrary Instance
    {
        get
        {
            lock (InstanceLock)
            {
                return _instance ??= new SevenZipLibrary(ResolveDefaultPath());
            }
        }
    }

    public static string ResolveDefaultPath()
    {
        var candidate = System.IO.Path.Combine(AppContext.BaseDirectory, "7z.dll");
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException("The archive engine (7z.dll) was not found next to the application.", candidate);
        }

        return candidate;
    }

    public T CreateObject<T>(ArchiveFormat format)
        where T : class
    {
        var classId = format.ClassId;
        var interfaceId = typeof(T).GUID;
        IntPtr pointer;
        var hr = _createObject(&classId, &interfaceId, &pointer);
        if (hr != HResult.Ok || pointer == IntPtr.Zero)
        {
            throw new ArchiveException($"The {format.Name} handler could not be created (0x{hr:X8}).");
        }

        try
        {
            return (T)Marshal.GetObjectForIUnknown(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static List<ArchiveFormat> ReadFormats(
        delegate* unmanaged<uint*, int> getNumberOfFormats,
        delegate* unmanaged<uint, uint, PropVariant*, int> getHandlerProperty)
    {
        uint count;
        Check(getNumberOfFormats(&count));

        var formats = new List<ArchiveFormat>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var name = ReadString(getHandlerProperty, i, HandlerProperty.Name);
            var classIdBytes = ReadBinary(getHandlerProperty, i, HandlerProperty.ClassId);
            if (name is null || classIdBytes is not { Length: 16 })
            {
                continue;
            }

            var extensions = (ReadString(getHandlerProperty, i, HandlerProperty.Extension) ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.ToLowerInvariant())
                .ToArray();

            var signatures = new List<byte[]>();
            if (ReadBinary(getHandlerProperty, i, HandlerProperty.Signature) is { Length: > 0 } signature)
            {
                signatures.Add(signature);
            }

            if (ReadBinary(getHandlerProperty, i, HandlerProperty.MultiSignature) is { Length: > 0 } multi)
            {
                signatures.AddRange(SplitMultiSignature(multi));
            }

            var canUpdate = ReadValue(getHandlerProperty, i, HandlerProperty.Update).AsBool() ?? false;
            var signatureOffset = (int)(ReadValue(getHandlerProperty, i, HandlerProperty.SignatureOffset).AsUInt32() ?? 0);

            formats.Add(FormatCatalog.Describe(new Guid(classIdBytes), name, extensions, signatures, signatureOffset, canUpdate));
        }

        return formats;
    }

    private static IEnumerable<byte[]> SplitMultiSignature(byte[] data)
    {
        var position = 0;
        while (position < data.Length)
        {
            int length = data[position++];
            if (length == 0 || position + length > data.Length)
            {
                yield break;
            }

            yield return data[position..(position + length)];
            position += length;
        }
    }

    private static PropVariant ReadValue(delegate* unmanaged<uint, uint, PropVariant*, int> getter, uint index, uint propId)
    {
        PropVariant value = default;
        if (getter(index, propId, &value) != HResult.Ok)
        {
            value.Clear();
        }

        return value;
    }

    private static string? ReadString(delegate* unmanaged<uint, uint, PropVariant*, int> getter, uint index, uint propId)
    {
        var value = ReadValue(getter, index, propId);
        try
        {
            return value.AsString();
        }
        finally
        {
            value.Clear();
        }
    }

    private static byte[]? ReadBinary(delegate* unmanaged<uint, uint, PropVariant*, int> getter, uint index, uint propId)
    {
        var value = ReadValue(getter, index, propId);
        try
        {
            return value.AsBinary();
        }
        finally
        {
            value.Clear();
        }
    }

    private static void Check(int hr)
    {
        if (hr != HResult.Ok)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }
}
