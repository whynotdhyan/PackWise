namespace PackWise.Core.Security;

[Flags]
public enum FileRisk
{
    None = 0,
    Executable = 1 << 0,
    Script = 1 << 1,
    Shortcut = 1 << 2,
    MacroDocument = 1 << 3,
    DiskImage = 1 << 4,
    Installer = 1 << 5,
    DisguisedExtension = 1 << 6,
    SpoofedName = 1 << 7,
}

/// <summary>
/// Flags file types that can run code or hide what they are. These are informational
/// signals shown before extraction or opening; they are not a malware verdict.
/// </summary>
public static class FileRiskClassifier
{
    private static readonly HashSet<string> Executables = Set("exe", "com", "scr", "pif", "cpl", "dll", "sys", "ocx", "drv", "efi");
    private static readonly HashSet<string> Scripts = Set("bat", "cmd", "ps1", "psm1", "psd1", "vbs", "vbe", "js", "jse", "wsf", "wsh", "wsc", "hta", "reg", "msh", "scf", "inf", "jar", "sct", "chm");
    private static readonly HashSet<string> Shortcuts = Set("lnk", "url", "website", "library-ms", "searchconnector-ms", "settingcontent-ms", "appref-ms");
    private static readonly HashSet<string> MacroDocuments = Set("docm", "dotm", "xlsm", "xltm", "xlam", "xlsb", "pptm", "potm", "ppam", "ppsm", "sldm", "one", "accde", "mdb", "accdb");
    private static readonly HashSet<string> DiskImages = Set("iso", "img", "vhd", "vhdx");
    private static readonly HashSet<string> Installers = Set("msi", "msix", "msixbundle", "appx", "appxbundle", "msp", "mst", "application", "appinstaller");
    private static readonly HashSet<string> DecoyExtensions = Set("pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "jpg", "jpeg", "png", "gif", "mp3", "mp4", "zip", "rar", "csv", "rtf");

    public static FileRisk Classify(string fileName)
    {
        var risk = FileRisk.None;
        var name = Path.GetFileName(fileName.TrimEnd('\\', '/'));

        if (name.Any(c => c is '‪' or '‫' or '‬' or '‭' or '‮' or '⁦' or '⁧' or '⁨' or '⁩'))
        {
            risk |= FileRisk.SpoofedName;
        }

        var extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        if (Executables.Contains(extension))
        {
            risk |= FileRisk.Executable;
        }
        else if (Scripts.Contains(extension))
        {
            risk |= FileRisk.Script;
        }
        else if (Shortcuts.Contains(extension))
        {
            risk |= FileRisk.Shortcut;
        }
        else if (MacroDocuments.Contains(extension))
        {
            risk |= FileRisk.MacroDocument;
        }
        else if (DiskImages.Contains(extension))
        {
            risk |= FileRisk.DiskImage;
        }
        else if (Installers.Contains(extension))
        {
            risk |= FileRisk.Installer;
        }

        if ((risk & (FileRisk.Executable | FileRisk.Script | FileRisk.Shortcut | FileRisk.Installer)) != 0 && HasDecoyExtension(name))
        {
            risk |= FileRisk.DisguisedExtension;
        }

        return risk;
    }

    public static bool CanRunCode(FileRisk risk) =>
        (risk & (FileRisk.Executable | FileRisk.Script | FileRisk.Shortcut | FileRisk.Installer | FileRisk.MacroDocument)) != 0;

    /// <summary>A short, plain-language description such as "A program disguised as a document".</summary>
    public static string Describe(FileRisk risk)
    {
        var what = risk switch
        {
            _ when risk.HasFlag(FileRisk.DisguisedExtension) && risk.HasFlag(FileRisk.Script) => "A script disguised as a document (double extension)",
            _ when risk.HasFlag(FileRisk.DisguisedExtension) && risk.HasFlag(FileRisk.Shortcut) => "A shortcut disguised as a document (double extension)",
            _ when risk.HasFlag(FileRisk.DisguisedExtension) => "A program disguised as a document (double extension)",
            _ when risk.HasFlag(FileRisk.Executable) => "A program",
            _ when risk.HasFlag(FileRisk.Script) => "A script",
            _ when risk.HasFlag(FileRisk.Shortcut) => "A shortcut that can launch other programs",
            _ when risk.HasFlag(FileRisk.MacroDocument) => "A document that can contain macros",
            _ when risk.HasFlag(FileRisk.Installer) => "An installer package",
            _ when risk.HasFlag(FileRisk.DiskImage) => "A disk image that can hide other files",
            _ => null,
        };

        if (risk.HasFlag(FileRisk.SpoofedName))
        {
            const string spoof = "its name uses hidden characters to fake the extension";
            return what is null ? char.ToUpperInvariant(spoof[0]) + spoof[1..] : $"{what}; {spoof}";
        }

        return what ?? string.Empty;
    }

    private static bool HasDecoyExtension(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var inner = Path.GetExtension(stem).TrimStart('.').ToLowerInvariant();
        return inner.Length > 0 && DecoyExtensions.Contains(inner);
    }

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.OrdinalIgnoreCase);
}
