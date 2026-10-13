namespace PackWise.Archiving.SevenZip.Interop;

/// <summary>Item and archive property identifiers (7-Zip PropID.h).</summary>
internal static class ItemProperty
{
    public const uint MainSubfile = 1;
    public const uint Path = 3;
    public const uint Name = 4;
    public const uint IsDir = 6;
    public const uint Size = 7;
    public const uint PackSize = 8;
    public const uint Attrib = 9;
    public const uint CTime = 10;
    public const uint ATime = 11;
    public const uint MTime = 12;
    public const uint Solid = 13;
    public const uint Encrypted = 15;
    public const uint Crc = 19;
    public const uint IsAnti = 21;
    public const uint Method = 22;
    public const uint HostOS = 23;
    public const uint Comment = 28;
    public const uint IsVolume = 35;
    public const uint Offset = 36;
    public const uint NumVolumes = 39;
    public const uint PhySize = 44;
    public const uint HeadersSize = 45;
    public const uint PosixAttrib = 53;
    public const uint SymLink = 54;
    public const uint Error = 55;
    public const uint IsAltStream = 63;
    public const uint ErrorFlags = 71;
    public const uint WarningFlags = 72;
    public const uint Warning = 73;
    public const uint HardLink = 90;
}

/// <summary>Format handler properties exposed through GetHandlerProperty2.</summary>
internal static class HandlerProperty
{
    public const uint Name = 0;
    public const uint ClassId = 1;
    public const uint Extension = 2;
    public const uint AddExtension = 3;
    public const uint Update = 4;
    public const uint Signature = 6;
    public const uint MultiSignature = 7;
    public const uint SignatureOffset = 8;
    public const uint Flags = 11;
}

internal static class AskMode
{
    public const int Extract = 0;
    public const int Test = 1;
    public const int Skip = 2;
}

internal static class OperationResultCode
{
    public const int Ok = 0;
    public const int UnsupportedMethod = 1;
    public const int DataError = 2;
    public const int CrcError = 3;
    public const int Unavailable = 4;
    public const int UnexpectedEnd = 5;
    public const int DataAfterEnd = 6;
    public const int IsNotArc = 7;
    public const int HeadersError = 8;
    public const int WrongPassword = 9;
}
