using System.Runtime.InteropServices;

// Vtable layouts mirror 7-Zip's IStream.h, IArchive.h, IPassword.h and IProgress.h.
// Derived interfaces redeclare inherited members because ComImport does not chain vtables.
namespace PackWise.Archiving.SevenZip.Interop;

[ComImport]
[Guid("23170F69-40C1-278A-0000-000300010000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISequentialInStream
{
    [PreserveSig]
    int Read(IntPtr data, uint size, IntPtr processedSize);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000300020000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISequentialOutStream
{
    [PreserveSig]
    int Write(IntPtr data, uint size, IntPtr processedSize);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000300030000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInStream
{
    [PreserveSig]
    int Read(IntPtr data, uint size, IntPtr processedSize);

    [PreserveSig]
    int Seek(long offset, uint seekOrigin, IntPtr newPosition);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000300040000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOutStream
{
    [PreserveSig]
    int Write(IntPtr data, uint size, IntPtr processedSize);

    [PreserveSig]
    int Seek(long offset, uint seekOrigin, IntPtr newPosition);

    [PreserveSig]
    int SetSize(ulong newSize);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000300060000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IStreamGetSize
{
    [PreserveSig]
    int GetSize(out ulong size);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000000050000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IProgress
{
    [PreserveSig]
    int SetTotal(ulong total);

    [PreserveSig]
    int SetCompleted(IntPtr completeValue);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000500100000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICryptoGetTextPassword
{
    [PreserveSig]
    int CryptoGetTextPassword([MarshalAs(UnmanagedType.BStr)] out string? password);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000500110000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICryptoGetTextPassword2
{
    [PreserveSig]
    int CryptoGetTextPassword2(out int passwordIsDefined, [MarshalAs(UnmanagedType.BStr)] out string? password);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000600100000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IArchiveOpenCallback
{
    [PreserveSig]
    int SetTotal(IntPtr files, IntPtr bytes);

    [PreserveSig]
    int SetCompleted(IntPtr files, IntPtr bytes);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000600200000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IArchiveExtractCallback
{
    [PreserveSig]
    int SetTotal(ulong total);

    [PreserveSig]
    int SetCompleted(IntPtr completeValue);

    [PreserveSig]
    int GetStream(uint index, out ISequentialOutStream? outStream, int askExtractMode);

    [PreserveSig]
    int PrepareOperation(int askExtractMode);

    [PreserveSig]
    int SetOperationResult(int operationResult);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000600300000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IArchiveOpenVolumeCallback
{
    [PreserveSig]
    int GetProperty(uint propId, IntPtr value);

    [PreserveSig]
    int GetStream([MarshalAs(UnmanagedType.LPWStr)] string name, out IInStream? inStream);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000600400000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInArchiveGetStream
{
    [PreserveSig]
    int GetStream(uint index, out ISequentialInStream? stream);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000600600000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInArchive
{
    [PreserveSig]
    int Open(IInStream stream, IntPtr maxCheckStartPosition, IArchiveOpenCallback? openCallback);

    [PreserveSig]
    int Close();

    [PreserveSig]
    int GetNumberOfItems(out uint count);

    [PreserveSig]
    int GetProperty(uint index, uint propId, IntPtr value);

    [PreserveSig]
    int Extract([MarshalAs(UnmanagedType.LPArray)] uint[]? indices, uint numItems, int testMode, IArchiveExtractCallback extractCallback);

    [PreserveSig]
    int GetArchiveProperty(uint propId, IntPtr value);

    [PreserveSig]
    int GetNumberOfProperties(out uint count);

    [PreserveSig]
    int GetPropertyInfo(uint index, [MarshalAs(UnmanagedType.BStr)] out string? name, out uint propId, out ushort varType);

    [PreserveSig]
    int GetNumberOfArchiveProperties(out uint count);

    [PreserveSig]
    int GetArchivePropertyInfo(uint index, [MarshalAs(UnmanagedType.BStr)] out string? name, out uint propId, out ushort varType);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000600800000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IArchiveUpdateCallback
{
    [PreserveSig]
    int SetTotal(ulong total);

    [PreserveSig]
    int SetCompleted(IntPtr completeValue);

    [PreserveSig]
    int GetUpdateItemInfo(uint index, out int newData, out int newProperties, out uint indexInArchive);

    [PreserveSig]
    int GetProperty(uint index, uint propId, IntPtr value);

    [PreserveSig]
    int GetStream(uint index, out ISequentialInStream? inStream);

    [PreserveSig]
    int SetOperationResult(int operationResult);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000600A00000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOutArchive
{
    [PreserveSig]
    int UpdateItems(ISequentialOutStream outStream, uint numItems, IArchiveUpdateCallback updateCallback);

    [PreserveSig]
    int GetFileTimeType(out uint type);
}

[ComImport]
[Guid("23170F69-40C1-278A-0000-000600030000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISetProperties
{
    [PreserveSig]
    int SetProperties(IntPtr names, IntPtr values, uint count);
}
