using System.Runtime.InteropServices;

namespace PackWise.Archiving.SevenZip.Interop;

internal enum VarType : ushort
{
    Empty = 0,
    I2 = 2,
    I4 = 3,
    Bstr = 8,
    Bool = 11,
    I1 = 16,
    UI1 = 17,
    UI2 = 18,
    UI4 = 19,
    I8 = 20,
    UI8 = 21,
    Int = 22,
    UInt = 23,
    FileTime = 64,
}

/// <summary>
/// Blittable PROPVARIANT layout for 64-bit processes (24 bytes).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public VarType Type;
    [FieldOffset(8)] public long Int64Value;
    [FieldOffset(8)] public IntPtr Pointer;

    public readonly bool IsEmpty => Type == VarType.Empty;

    public readonly string? AsString() =>
        Type == VarType.Bstr && Pointer != IntPtr.Zero ? Marshal.PtrToStringBSTR(Pointer) : null;

    public readonly byte[]? AsBinary()
    {
        if (Type != VarType.Bstr || Pointer == IntPtr.Zero)
        {
            return null;
        }

        var length = (int)NativeMethods.SysStringByteLen(Pointer);
        var bytes = new byte[length];
        Marshal.Copy(Pointer, bytes, 0, length);
        return bytes;
    }

    public readonly ulong? AsUInt64() => Type switch
    {
        VarType.UI1 => (byte)Int64Value,
        VarType.UI2 => (ushort)Int64Value,
        VarType.UI4 or VarType.UInt => (uint)Int64Value,
        VarType.UI8 => (ulong)Int64Value,
        VarType.I1 => (ulong)(sbyte)Int64Value,
        VarType.I2 => (ulong)(short)Int64Value,
        VarType.I4 or VarType.Int => (ulong)(int)Int64Value,
        VarType.I8 => (ulong)Int64Value,
        _ => null,
    };

    public readonly uint? AsUInt32() => AsUInt64() is { } value ? (uint)value : null;

    public readonly bool? AsBool() => Type == VarType.Bool ? (short)Int64Value != 0 : null;

    public readonly DateTime? AsDateTime()
    {
        if (Type != VarType.FileTime || Int64Value <= 0)
        {
            return null;
        }

        try
        {
            return DateTime.FromFileTimeUtc(Int64Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static PropVariant FromString(string value) =>
        new() { Type = VarType.Bstr, Pointer = Marshal.StringToBSTR(value) };

    public static PropVariant FromUInt32(uint value) => new() { Type = VarType.UI4, Int64Value = value };

    public static PropVariant FromUInt64(ulong value) => new() { Type = VarType.UI8, Int64Value = (long)value };

    public static PropVariant FromBool(bool value) => new() { Type = VarType.Bool, Int64Value = value ? -1 : 0 };

    public static PropVariant FromDateTime(DateTime value) =>
        new() { Type = VarType.FileTime, Int64Value = value.ToFileTimeUtc() };

    public static unsafe void Write(IntPtr destination, in PropVariant value) => *(PropVariant*)destination = value;

    public void Clear()
    {
        NativeMethods.PropVariantClear(ref this);
        this = default;
    }
}
