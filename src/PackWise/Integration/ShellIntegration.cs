using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PackWise.Integration;

/// <summary>A command offered in the Explorer right-click menu.</summary>
public sealed record ShellVerb(string Key, string Label, string Argument, bool ArchivesOnly, bool FilesOnly = false, bool FoldersOnly = false, bool SeparatorBefore = false);

/// <summary>
/// Per-user Windows integration: Explorer context menu, "Open with" associations, shortcuts and the
/// Apps &amp; features entry. Everything lives under HKEY_CURRENT_USER, needs no administrator rights,
/// and is removed completely by <see cref="RemoveAll"/>.
/// </summary>
public static partial class ShellIntegration
{
    public const string ProgId = "NorthstarLabs.PackWise.Archive";
    private const string MenuKey = "PackWise";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NorthstarLabs.PackWise";

    public static readonly IReadOnlyList<string> ArchiveExtensions =
    [
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".zst", ".tzst", ".lzma",
        ".cab", ".iso", ".wim", ".arj", ".lzh", ".lha", ".cpio", ".z", ".001",
    ];

    public static readonly IReadOnlyList<ShellVerb> Verbs =
    [
        new("open", "Open with PackWise", "--open", ArchivesOnly: true),
        new("extracthere", "Extract here", "--extract-here", ArchivesOnly: true, SeparatorBefore: true),
        new("extractfolder", "Extract to its own folder", "--extract-folder", ArchivesOnly: true),
        new("extractto", "Extract to…", "--extract-to", ArchivesOnly: true),
        new("test", "Test archive", "--test", ArchivesOnly: true),
        new("tozip", "Convert to ZIP", "--convert-zip", ArchivesOnly: true, SeparatorBefore: true),
        new("to7z", "Convert to 7Z", "--convert-7z", ArchivesOnly: true),
        new("add", "Add to archive…", "--add", ArchivesOnly: false, SeparatorBefore: true),
        new("zip", "Compress to ZIP file", "--zip", ArchivesOnly: false),
        new("sevenz", "Compress to 7Z file", "--7z", ArchivesOnly: false),
        new("checksum", "Checksums (SHA-256)", "--checksum", ArchivesOnly: false, FilesOnly: true, SeparatorBefore: true),
        new("duplicates", "Find duplicates here", "--duplicates", ArchivesOnly: false, FoldersOnly: true),
    ];

    private static string Executable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PackWise.exe");

    public static string InstallDirectory => AppContext.BaseDirectory.TrimEnd('\\');

    public static string DefaultInstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PackWise");

    // ---------------------------------------------------------------- Context menu

    public static bool IsContextMenuRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\*\shell\{MenuKey}");
        return key is not null;
    }

    /// <summary>
    /// Adds a "PackWise" submenu for files and folders. On Windows 11 it appears under
    /// "Show more options" (Shift+F10); the compact menu requires a signed app package.
    /// </summary>
    public static void RegisterContextMenu()
    {
        var appliesToArchives = string.Join(" OR ", ArchiveExtensions.Select(e => $"System.FileExtension:={e}"));
        WriteMenu(@"Software\Classes\*\shell", Verbs.Where(v => !v.FoldersOnly), appliesToArchives);
        WriteMenu(@"Software\Classes\Directory\shell", Verbs.Where(v => !v.ArchivesOnly && !v.FilesOnly), null);
        NotifyAssociationsChanged();
    }

    public static void UnregisterContextMenu()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\*\shell\{MenuKey}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Directory\shell\{MenuKey}", throwOnMissingSubKey: false);
        NotifyAssociationsChanged();
    }

    private static void WriteMenu(string parent, IEnumerable<ShellVerb> verbs, string? appliesToArchives)
    {
        using var menu = Registry.CurrentUser.CreateSubKey($@"{parent}\{MenuKey}");
        menu.SetValue("MUIVerb", "PackWise");
        menu.SetValue("Icon", $"\"{Executable}\",0");
        menu.SetValue("SubCommands", string.Empty);
        menu.SetValue("MultiSelectModel", "Player");

        var order = 0;
        foreach (var verb in verbs)
        {
            using var item = menu.CreateSubKey($@"shell\{++order:00}{verb.Key}");
            item.SetValue("MUIVerb", verb.Label);
            item.SetValue("MultiSelectModel", "Player");
            if (verb.SeparatorBefore && order > 1)
            {
                item.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord);
            }

            if (verb.ArchivesOnly && appliesToArchives is not null)
            {
                item.SetValue("AppliesTo", appliesToArchives);
            }

            if (verb.Key == "open")
            {
                item.SetValue("Icon", $"\"{Executable}\",0");
            }

            using var command = item.CreateSubKey("command");
            command.SetValue(string.Empty, $"\"{Executable}\" {verb.Argument} \"%1\"");
        }
    }

    // ---------------------------------------------------------------- File associations

    public static bool AreAssociationsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}");
        return key is not null;
    }

    /// <summary>
    /// Offers PackWise in "Open with" for archive types. Windows only lets the user choose the
    /// default app, so this never takes over a file type silently.
    /// </summary>
    public static void RegisterAssociations()
    {
        using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progId.SetValue(string.Empty, "Archive (PackWise)");
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(string.Empty, $"\"{Executable}\",0");
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(string.Empty, $"\"{Executable}\" --open \"%1\"");
        }

        using (var app = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\PackWise.exe"))
        {
            app.SetValue("FriendlyAppName", "PackWise");
            using var command = app.CreateSubKey(@"shell\open\command");
            command.SetValue(string.Empty, $"\"{Executable}\" --open \"%1\"");
            using var types = app.CreateSubKey("SupportedTypes");
            foreach (var extension in ArchiveExtensions)
            {
                types.SetValue(extension, string.Empty);
            }
        }

        foreach (var extension in ArchiveExtensions)
        {
            using var openWith = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        NotifyAssociationsChanged();
    }

    public static void UnregisterAssociations()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Applications\PackWise.exe", throwOnMissingSubKey: false);
        foreach (var extension in ArchiveExtensions)
        {
            using var openWith = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids", writable: true);
            openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
        }

        NotifyAssociationsChanged();
    }

    // ---------------------------------------------------------------- Shortcuts and uninstall entry

    public static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "PackWise.lnk");

    public static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PackWise.lnk");

    public static void CreateShortcut(string path) =>
        ShortcutWriter.Create(path, Executable, string.Empty, "Open, create and clean up archives safely", InstallDirectory);

    public static void RegisterUninstallEntry(string version)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "PackWise");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "Northstar Labs");
        key.SetValue("DisplayIcon", $"\"{Executable}\",0");
        key.SetValue("InstallLocation", InstallDirectory);
        key.SetValue("UninstallString", $"\"{Executable}\" --uninstall");
        key.SetValue("URLInfoAbout", Services.AppLinks.Repository);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(DirectorySize(InstallDirectory) / 1024), RegistryValueKind.DWord);
    }

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key is not null;
    }

    /// <summary>Removes every trace of integration. Files are deleted separately by the uninstaller.</summary>
    public static void RemoveAll()
    {
        UnregisterContextMenu();
        UnregisterAssociations();
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        foreach (var shortcut in new[] { StartMenuShortcut, DesktopShortcut })
        {
            if (File.Exists(shortcut))
            {
                File.Delete(shortcut);
            }
        }
    }

    private static long DirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void NotifyAssociationsChanged() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
