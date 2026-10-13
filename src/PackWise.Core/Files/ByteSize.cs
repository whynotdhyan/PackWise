using System.Globalization;

namespace PackWise.Core.Files;

public static class ByteSize
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Compact size such as "1.24 GB" using binary multiples, the way Explorer shows them.</summary>
    public static string Format(ulong bytes)
    {
        if (bytes < 1024)
        {
            return bytes == 1 ? "1 byte" : $"{bytes.ToString(CultureInfo.CurrentCulture)} bytes";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
        return $"{value.ToString(format, CultureInfo.CurrentCulture)} {Units[unit]}";
    }

    public static string Format(long bytes) => Format((ulong)Math.Max(0, bytes));

    /// <summary>Exact byte count with digit grouping, for detail views.</summary>
    public static string FormatExact(ulong bytes) => bytes.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Throughput such as "84.2 MB/s".</summary>
    public static string FormatRate(double bytesPerSecond) =>
        bytesPerSecond <= 0 ? string.Empty : Format((ulong)bytesPerSecond) + "/s";

    public static string FormatRatio(ulong packed, ulong original) =>
        original == 0 ? "—" : ((double)packed / original).ToString("P0", CultureInfo.CurrentCulture);
}
