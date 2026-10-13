using System.Runtime.InteropServices;

namespace PackWise.Archiving.SevenZip.Interop;

internal static partial class NativeMethods
{
    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(ref PropVariant value);

    [LibraryImport("oleaut32.dll")]
    public static partial uint SysStringByteLen(IntPtr bstr);
}

internal static class HResult
{
    public const int Ok = 0;
    public const int False = 1;
    public const int NotImplemented = unchecked((int)0x80004001);
    public const int Abort = unchecked((int)0x80004004);
    public const int Fail = unchecked((int)0x80004005);
    public const int InvalidArgument = unchecked((int)0x80070057);
    public const int OutOfMemory = unchecked((int)0x8007000E);
}
