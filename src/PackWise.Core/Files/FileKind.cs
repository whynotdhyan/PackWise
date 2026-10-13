namespace PackWise.Core.Files;

public enum FileKind
{
    Folder,
    Archive,
    Image,
    Audio,
    Video,
    Document,
    Spreadsheet,
    Presentation,
    Pdf,
    Code,
    Text,
    Executable,
    Font,
    DiskImage,
    Generic,
}

public static class FileKinds
{
    private static readonly Dictionary<string, FileKind> ByExtension = Build();

    public static FileKind FromName(string name, bool isDirectory = false)
    {
        if (isDirectory)
        {
            return FileKind.Folder;
        }

        var extension = Path.GetExtension(name).TrimStart('.');
        return ByExtension.GetValueOrDefault(extension, FileKind.Generic);
    }

    private static Dictionary<string, FileKind> Build()
    {
        var map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
        void Add(FileKind kind, params string[] extensions)
        {
            foreach (var extension in extensions)
            {
                map[extension] = kind;
            }
        }

        Add(FileKind.Archive, "zip", "7z", "rar", "tar", "gz", "tgz", "bz2", "tbz", "tbz2", "xz", "txz", "zst", "tzst", "lzma", "cab", "arj", "lzh", "lha", "z", "wim", "cpio", "001", "zipx", "jar", "apk", "xpi");
        Add(FileKind.Image, "png", "jpg", "jpeg", "gif", "bmp", "webp", "tif", "tiff", "ico", "svg", "heic", "heif", "avif", "raw", "cr2", "nef", "arw", "dng", "psd");
        Add(FileKind.Audio, "mp3", "wav", "flac", "aac", "ogg", "oga", "m4a", "wma", "opus", "aiff", "mid", "midi");
        Add(FileKind.Video, "mp4", "mkv", "avi", "mov", "wmv", "webm", "m4v", "flv", "mpg", "mpeg", "3gp", "ts");
        Add(FileKind.Document, "doc", "docx", "docm", "odt", "rtf", "pages", "wpd", "epub");
        Add(FileKind.Spreadsheet, "xls", "xlsx", "xlsm", "xlsb", "ods", "csv", "numbers");
        Add(FileKind.Presentation, "ppt", "pptx", "pptm", "odp", "key");
        Add(FileKind.Pdf, "pdf", "xps", "oxps");
        Add(FileKind.Code, "cs", "js", "ts", "tsx", "jsx", "py", "java", "c", "h", "cpp", "hpp", "rs", "go", "rb", "php", "swift", "kt", "html", "htm", "css", "scss", "xml", "json", "yaml", "yml", "toml", "sql", "sh", "ps1", "bat", "cmd", "xaml", "csproj", "sln", "lua", "dart");
        Add(FileKind.Text, "txt", "md", "log", "ini", "cfg", "conf", "nfo", "diz", "readme", "srt", "sub", "tex");
        Add(FileKind.Executable, "exe", "msi", "msix", "appx", "dll", "com", "scr", "sys", "lnk", "vbs", "jse", "hta", "reg", "appinstaller");
        Add(FileKind.Font, "ttf", "otf", "woff", "woff2", "fon");
        Add(FileKind.DiskImage, "iso", "img", "vhd", "vhdx", "vmdk", "dmg", "udf", "bin", "cue");
        return map;
    }
}
