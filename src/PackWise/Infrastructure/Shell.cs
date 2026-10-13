using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace PackWise.Infrastructure;

/// <summary>Small wrappers over Windows Shell behaviour: opening, revealing, type names.</summary>
public static partial class Shell
{
    private const uint ShgfiTypeName = 0x400;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint FileAttributeDirectory = 0x10;

    private static readonly ConcurrentDictionary<string, string> TypeNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opens a file or folder with its default handler.</summary>
    public static void Open(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Opens a web link in the default browser. Only https links are allowed.</summary>
    public static void OpenLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>Opens Explorer with the item selected.</summary>
    public static void Reveal(string path)
    {
        var argument = File.Exists(path) || Directory.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", argument) { UseShellExecute = true })?.Dispose();
    }

    public static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException)
        {
            // The clipboard can be briefly locked by another application.
        }
    }

    /// <summary>Windows' own description for a file type ("Text Document"), cached per extension.</summary>
    public static string TypeName(string name, bool isDirectory)
    {
        if (isDirectory)
        {
            return "File folder";
        }

        var extension = Path.GetExtension(name);
        if (string.IsNullOrEmpty(extension))
        {
            return "File";
        }

        return TypeNames.GetOrAdd(extension, static ext =>
        {
            var info = default(ShFileInfo);
            var result = SHGetFileInfo(ext, FileAttributeNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiTypeName | ShgfiUseFileAttributes);
            var typeName = result != IntPtr.Zero ? info.TypeName : null;
            return string.IsNullOrWhiteSpace(typeName) ? $"{ext.TrimStart('.').ToUpperInvariant()} File" : typeName;
        });
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShFileInfo info, uint size, uint flags);
}
