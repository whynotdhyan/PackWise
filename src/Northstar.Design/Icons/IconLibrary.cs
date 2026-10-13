using System.Windows.Media;

namespace Northstar.Design.Icons;

public enum IconKind
{
    None,
    Add,
    Extract,
    Test,
    View,
    Delete,
    Find,
    Info,
    Duplicates,
    Checksum,
    Settings,
    Back,
    Forward,
    Up,
    Refresh,
    Folder,
    FolderOpen,
    Computer,
    Drive,
    Lock,
    Warning,
    Globe,
    Success,
    Error,
    Star,
    Copy,
    External,
    Coffee,
    Archive,
    Moon,
    Sun,
    Plus,
    Minus,
    Close,
    ChevronRight,
    ChevronDown,
    Shield,
    Filter,
    Recycle,
    Move,
    Help,
    Key,
    Eye,
    EyeOff,
    Sparkle,
    Layers,
    Document,
    List,
}

/// <summary>A glyph on a 24×24 grid: stroked outline plus an optional tinted fill underneath.</summary>
public sealed record IconDefinition(Geometry? Fill, Geometry Stroke);

/// <summary>
/// The Northstar Labs line-icon set. Strokes are drawn with round caps and joins at 1.6 units;
/// fills use the accent colour at reduced opacity to give a calm two-tone look.
/// </summary>
public static class IconLibrary
{
    private static readonly Dictionary<IconKind, IconDefinition> Definitions = new()
    {
        [IconKind.Add] = Define(
            "M4 9.5h16v8.75a2.25 2.25 0 0 1-2.25 2.25H6.25A2.25 2.25 0 0 1 4 18.25z",
            "M2.75 9.5h18.5 M4 9.5v8.75a2.25 2.25 0 0 0 2.25 2.25h11.5A2.25 2.25 0 0 0 20 18.25V9.5 M12 3v10.5 M8.75 10.25L12 13.5l3.25-3.25 M9.5 16.75h5"),
        [IconKind.Extract] = Define(
            "M4 9.5h16v8.75a2.25 2.25 0 0 1-2.25 2.25H6.25A2.25 2.25 0 0 1 4 18.25z",
            "M2.75 9.5h18.5 M4 9.5v8.75a2.25 2.25 0 0 0 2.25 2.25h11.5A2.25 2.25 0 0 0 20 18.25V9.5 M12 14.5V3 M8.75 6.25L12 3l3.25 3.25 M9.5 16.75h5"),
        [IconKind.Test] = Define(
            "M12 3l7.5 3v5.25c0 4.6-3.1 8.4-7.5 9.75-4.4-1.35-7.5-5.15-7.5-9.75V6z",
            "M12 3l7.5 3v5.25c0 4.6-3.1 8.4-7.5 9.75-4.4-1.35-7.5-5.15-7.5-9.75V6z M8.75 12.25l2.25 2.25 4.25-4.5"),
        [IconKind.View] = Define(
            "M12 9a3 3 0 1 1 0 6 3 3 0 1 1 0-6z",
            "M2.5 12s3.5-6.5 9.5-6.5 9.5 6.5 9.5 6.5-3.5 6.5-9.5 6.5S2.5 12 2.5 12z M12 9a3 3 0 1 1 0 6 3 3 0 1 1 0-6z"),
        [IconKind.Delete] = Define(
            "M6.25 7l.95 12.4a1.6 1.6 0 0 0 1.6 1.5h6.4a1.6 1.6 0 0 0 1.6-1.5L17.75 7z",
            "M4 7h16 M9.25 7V4.75a1 1 0 0 1 1-1h3.5a1 1 0 0 1 1 1V7 M6.25 7l.95 12.4a1.6 1.6 0 0 0 1.6 1.5h6.4a1.6 1.6 0 0 0 1.6-1.5L17.75 7 M10 11v5.5 M14 11v5.5"),
        [IconKind.Find] = Define(
            "M10.5 4a6.5 6.5 0 1 1 0 13 6.5 6.5 0 1 1 0-13z",
            "M10.5 4a6.5 6.5 0 1 1 0 13 6.5 6.5 0 1 1 0-13z M15.25 15.25L20.5 20.5"),
        [IconKind.Info] = Define(
            "M12 3a9 9 0 1 1 0 18 9 9 0 1 1 0-18z",
            "M12 3a9 9 0 1 1 0 18 9 9 0 1 1 0-18z M12 11v5.5 M12 7.75v.01"),
        [IconKind.Help] = Define(
            "M12 3a9 9 0 1 1 0 18 9 9 0 1 1 0-18z",
            "M12 3a9 9 0 1 1 0 18 9 9 0 1 1 0-18z M9.5 9.5a2.6 2.6 0 0 1 5.1.6c0 1.75-2.6 2.2-2.6 3.9 M12 17v.01"),
        [IconKind.Duplicates] = Define(
            "M5.5 7.5h7.25l3.75 3.75v8.25a1.5 1.5 0 0 1-1.5 1.5H5.5A1.5 1.5 0 0 1 4 19.5V9a1.5 1.5 0 0 1 1.5-1.5z",
            "M8 4.5V4a1.5 1.5 0 0 1 1.5-1.5h7.25l3.75 3.75v9.25a1.5 1.5 0 0 1-1.5 1.5h-.5 M5.5 7.5h7.25l3.75 3.75v8.25a1.5 1.5 0 0 1-1.5 1.5H5.5A1.5 1.5 0 0 1 4 19.5V9a1.5 1.5 0 0 1 1.5-1.5z M7.5 14h5.5 M7.5 17h5.5"),
        [IconKind.Checksum] = Define(
            "M5.5 3h13A2.5 2.5 0 0 1 21 5.5v13a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 18.5v-13A2.5 2.5 0 0 1 5.5 3z",
            "M5.5 3h13A2.5 2.5 0 0 1 21 5.5v13a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 18.5v-13A2.5 2.5 0 0 1 5.5 3z M10.25 7l-1.25 10 M15 7l-1.25 10 M7.25 10h10.5 M6.75 14h10.5"),
        [IconKind.Settings] = Define(
            "M12 8.75a3.25 3.25 0 1 1 0 6.5 3.25 3.25 0 1 1 0-6.5z",
            "M10.29,5.21 L10.49,2.92 L13.51,2.92 L13.71,5.21 L15.59,5.99 L17.35,4.51 L19.49,6.65 L18.01,8.41 L18.79,10.29 L21.08,10.49 L21.08,13.51 L18.79,13.71 L18.01,15.59 L19.49,17.35 L17.35,19.49 L15.59,18.01 L13.71,18.79 L13.51,21.08 L10.49,21.08 L10.29,18.79 L8.41,18.01 L6.65,19.49 L4.51,17.35 L5.99,15.59 L5.21,13.71 L2.92,13.51 L2.92,10.49 L5.21,10.29 L5.99,8.41 L4.51,6.65 L6.65,4.51 L8.41,5.99Z M12 8.75a3.25 3.25 0 1 1 0 6.5 3.25 3.25 0 1 1 0-6.5z"),
        [IconKind.Back] = Define(null, "M14.5 5.5L8 12l6.5 6.5"),
        [IconKind.Forward] = Define(null, "M9.5 5.5L16 12l-6.5 6.5"),
        [IconKind.Up] = Define(null, "M12 19.5V5 M6 11l6-6 6 6"),
        [IconKind.Refresh] = Define(null, "M19.5 12a7.5 7.5 0 1 1-2.2-5.3 M19.75 3.75v4.5h-4.5"),
        [IconKind.Folder] = Define(
            "M3 9h18v8.75A2.25 2.25 0 0 1 18.75 20H5.25A2.25 2.25 0 0 1 3 17.75z",
            "M3 17.75V6.25A2.25 2.25 0 0 1 5.25 4h3.6a1.5 1.5 0 0 1 1.15.55L11.6 6.5h7.15A2.25 2.25 0 0 1 21 8.75v9A2.25 2.25 0 0 1 18.75 20H5.25A2.25 2.25 0 0 1 3 17.75z M3 9.5h18"),
        [IconKind.FolderOpen] = Define(
            "M5.2 11h16.3l-2.1 7.5a2 2 0 0 1-1.9 1.5H4.25z",
            "M3 17.5V6.25A2.25 2.25 0 0 1 5.25 4h3.6a1.5 1.5 0 0 1 1.15.55L11.6 6.5h5.65A2.25 2.25 0 0 1 19.5 8.75V11 M5.2 11h16.3l-2.1 7.5a2 2 0 0 1-1.9 1.5H4.25a1.25 1.25 0 0 1-1.2-1.6z"),
        [IconKind.Computer] = Define(
            "M4.25 4.5h15.5a1.25 1.25 0 0 1 1.25 1.25v9.5a1.25 1.25 0 0 1-1.25 1.25H4.25A1.25 1.25 0 0 1 3 15.25v-9.5A1.25 1.25 0 0 1 4.25 4.5z",
            "M4.25 4.5h15.5a1.25 1.25 0 0 1 1.25 1.25v9.5a1.25 1.25 0 0 1-1.25 1.25H4.25A1.25 1.25 0 0 1 3 15.25v-9.5A1.25 1.25 0 0 1 4.25 4.5z M8.5 20h7 M12 16.5V20"),
        [IconKind.Drive] = Define(
            "M3 14.5h18v3.25A1.75 1.75 0 0 1 19.25 19.5H4.75A1.75 1.75 0 0 1 3 17.75z",
            "M3 14.5l2.4-7.6A2 2 0 0 1 7.3 5.5h9.4a2 2 0 0 1 1.9 1.4L21 14.5 M3 14.5h18v3.25A1.75 1.75 0 0 1 19.25 19.5H4.75A1.75 1.75 0 0 1 3 17.75z M16.5 17h.01 M13.75 17h.01"),
        [IconKind.Lock] = Define(
            "M6.25 10.5h11.5a1.75 1.75 0 0 1 1.75 1.75v6.5a1.75 1.75 0 0 1-1.75 1.75H6.25a1.75 1.75 0 0 1-1.75-1.75v-6.5a1.75 1.75 0 0 1 1.75-1.75z",
            "M6.25 10.5h11.5a1.75 1.75 0 0 1 1.75 1.75v6.5a1.75 1.75 0 0 1-1.75 1.75H6.25a1.75 1.75 0 0 1-1.75-1.75v-6.5a1.75 1.75 0 0 1 1.75-1.75z M8 10.5V7.75a4 4 0 0 1 8 0v2.75 M12 14.25v2"),
        [IconKind.Key] = Define(
            "M8 9.5a4 4 0 1 1 0 8 4 4 0 1 1 0-8z",
            "M8 9.5a4 4 0 1 1 0 8 4 4 0 1 1 0-8z M11 11.25l8.5-5 M16.5 8l1.75 2.5 M14 9.5l1.5 2"),
        [IconKind.Warning] = Define(
            "M10.3 4.3L2.9 17.2A2 2 0 0 0 4.6 20.2h14.8a2 2 0 0 0 1.7-3L13.7 4.3a2 2 0 0 0-3.4 0z",
            "M10.3 4.3L2.9 17.2A2 2 0 0 0 4.6 20.2h14.8a2 2 0 0 0 1.7-3L13.7 4.3a2 2 0 0 0-3.4 0z M12 9.5v4.5 M12 17.25v.01"),
        [IconKind.Globe] = Define(
            "M12 3a9 9 0 1 1 0 18 9 9 0 1 1 0-18z",
            "M12 3a9 9 0 1 1 0 18 9 9 0 1 1 0-18z M3.5 9h17 M3.5 15h17 M12 3c2.4 2.5 3.7 5.6 3.7 9s-1.3 6.5-3.7 9c-2.4-2.5-3.7-5.6-3.7-9S9.6 5.5 12 3z"),
        [IconKind.Success] = Define(
            "M12 3a9 9 0 1 1 0 18 9 9 0 1 1 0-18z",
            "M12 3a9 9 0 1 1 0 18 9 9 0 1 1 0-18z M8.25 12.25l2.5 2.5 5-5.25"),
        [IconKind.Error] = Define(
            "M12 3a9 9 0 1 1 0 18 9 9 0 1 1 0-18z",
            "M12 3a9 9 0 1 1 0 18 9 9 0 1 1 0-18z M9.25 9.25l5.5 5.5 M14.75 9.25l-5.5 5.5"),
        [IconKind.Star] = Define(
            "M12.00,3.20 L14.35,9.16 L20.75,9.56 L15.80,13.64 L17.41,19.84 L12.00,16.40 L6.59,19.84 L8.20,13.64 L3.25,9.56 L9.65,9.16Z",
            "M12.00,3.20 L14.35,9.16 L20.75,9.56 L15.80,13.64 L17.41,19.84 L12.00,16.40 L6.59,19.84 L8.20,13.64 L3.25,9.56 L9.65,9.16Z"),
        [IconKind.Copy] = Define(
            "M9.5 8.5h9a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 8 19v-9a1.5 1.5 0 0 1 1.5-1.5z",
            "M9.5 8.5h9a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 8 19v-9a1.5 1.5 0 0 1 1.5-1.5z M16 8.5V5.5A1.5 1.5 0 0 0 14.5 4h-9A1.5 1.5 0 0 0 4 5.5v9A1.5 1.5 0 0 0 5.5 16H8"),
        [IconKind.External] = Define(
            null,
            "M13.5 4h6.5v6.5 M20 4l-8.5 8.5 M17.5 13.75v4.5A1.75 1.75 0 0 1 15.75 20h-10A1.75 1.75 0 0 1 4 18.25v-10A1.75 1.75 0 0 1 5.75 6.5h4.5"),
        [IconKind.Coffee] = Define(
            "M4.5 9.5h11.5v5.75a4.25 4.25 0 0 1-4.25 4.25h-3A4.25 4.25 0 0 1 4.5 15.25z",
            "M4.5 9.5h11.5v5.75a4.25 4.25 0 0 1-4.25 4.25h-3A4.25 4.25 0 0 1 4.5 15.25z M16 11h1.5a2.5 2.5 0 0 1 0 5H16 M8 3.5c-.8.9-.8 1.9 0 2.8 M11.5 3.5c-.8.9-.8 1.9 0 2.8"),
        [IconKind.Archive] = Define(
            "M3.75 4h16.5a1 1 0 0 1 1 1v2.75a1 1 0 0 1-1 1H3.75a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z",
            "M3.75 4h16.5a1 1 0 0 1 1 1v2.75a1 1 0 0 1-1 1H3.75a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z M4.25 8.75v9.5A1.75 1.75 0 0 0 6 20h12a1.75 1.75 0 0 0 1.75-1.75v-9.5 M10 12.25h4"),
        [IconKind.Moon] = Define(
            "M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z",
            "M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z"),
        [IconKind.Sun] = Define(
            "M12 8a4 4 0 1 1 0 8 4 4 0 1 1 0-8z",
            "M12 8a4 4 0 1 1 0 8 4 4 0 1 1 0-8z M12 2.5v2 M12 19.5v2 M2.5 12h2 M19.5 12h2 M5.3 5.3l1.4 1.4 M17.3 17.3l1.4 1.4 M5.3 18.7l1.4-1.4 M17.3 6.7l1.4-1.4"),
        [IconKind.Plus] = Define(null, "M12 5v14 M5 12h14"),
        [IconKind.Minus] = Define(null, "M5 12h14"),
        [IconKind.Close] = Define(null, "M6 6l12 12 M18 6L6 18"),
        [IconKind.ChevronRight] = Define(null, "M9.5 6l6 6-6 6"),
        [IconKind.ChevronDown] = Define(null, "M6 9.5l6 6 6-6"),
        [IconKind.Shield] = Define(
            "M12 3l7.5 3v5.25c0 4.6-3.1 8.4-7.5 9.75-4.4-1.35-7.5-5.15-7.5-9.75V6z",
            "M12 3l7.5 3v5.25c0 4.6-3.1 8.4-7.5 9.75-4.4-1.35-7.5-5.15-7.5-9.75V6z"),
        [IconKind.Filter] = Define(
            "M3.5 5h17l-6.5 7.75V19l-4 1.75v-8z",
            "M3.5 5h17l-6.5 7.75V19l-4 1.75v-8z"),
        [IconKind.Recycle] = Define(
            "M6.25 7l.95 12.4a1.6 1.6 0 0 0 1.6 1.5h6.4a1.6 1.6 0 0 0 1.6-1.5L17.75 7z",
            "M4 7h16 M9.25 7V4.75a1 1 0 0 1 1-1h3.5a1 1 0 0 1 1 1V7 M6.25 7l.95 12.4a1.6 1.6 0 0 0 1.6 1.5h6.4a1.6 1.6 0 0 0 1.6-1.5L17.75 7 M9.5 15.25a2.6 2.6 0 0 0 4.75.9 M14.5 12.75a2.6 2.6 0 0 0-4.75-.9 M14.6 14.4v1.9h-1.9 M9.4 11.6V9.7h1.9"),
        [IconKind.Move] = Define(
            "M3 9h18v8.75A2.25 2.25 0 0 1 18.75 20H5.25A2.25 2.25 0 0 1 3 17.75z",
            "M3 17.75V6.25A2.25 2.25 0 0 1 5.25 4h3.6a1.5 1.5 0 0 1 1.15.55L11.6 6.5h7.15A2.25 2.25 0 0 1 21 8.75v9A2.25 2.25 0 0 1 18.75 20H5.25A2.25 2.25 0 0 1 3 17.75z M8.5 14.25h7 M13 11.5l2.75 2.75L13 17"),
        [IconKind.Eye] = Define(
            "M12 9a3 3 0 1 1 0 6 3 3 0 1 1 0-6z",
            "M2.5 12s3.5-6.5 9.5-6.5 9.5 6.5 9.5 6.5-3.5 6.5-9.5 6.5S2.5 12 2.5 12z M12 9a3 3 0 1 1 0 6 3 3 0 1 1 0-6z"),
        [IconKind.EyeOff] = Define(
            null,
            "M10 5.7c.65-.13 1.3-.2 2-.2 6 0 9.5 6.5 9.5 6.5a17 17 0 0 1-2.4 3.2 M6.6 6.9C4 8.6 2.5 12 2.5 12s3.5 6.5 9.5 6.5c1.9 0 3.6-.65 5-1.6 M9.9 9.9a3 3 0 0 0 4.2 4.2 M3.5 3.5l17 17"),
        [IconKind.Sparkle] = Define(
            "M12.00,2.00 L13.84,10.16 L22.00,12.00 L13.84,13.84 L12.00,22.00 L10.16,13.84 L2.00,12.00 L10.16,10.16Z",
            "M12.00,2.00 L13.84,10.16 L22.00,12.00 L13.84,13.84 L12.00,22.00 L10.16,13.84 L2.00,12.00 L10.16,10.16Z"),
        [IconKind.Layers] = Define(
            "M12 3.5l9 4.75-9 4.75-9-4.75z",
            "M12 3.5l9 4.75-9 4.75-9-4.75z M3 12.25l9 4.75 9-4.75 M3 16.25l9 4.75 9-4.75"),
        [IconKind.Document] = Define(
            "M6.5 3h7.75L19 7.75v11.75A1.5 1.5 0 0 1 17.5 21h-11A1.5 1.5 0 0 1 5 19.5v-15A1.5 1.5 0 0 1 6.5 3z",
            "M6.5 3h7.75L19 7.75v11.75A1.5 1.5 0 0 1 17.5 21h-11A1.5 1.5 0 0 1 5 19.5v-15A1.5 1.5 0 0 1 6.5 3z M14 3v5h5 M8.5 12.5h7 M8.5 16h7"),
        [IconKind.List] = Define(null, "M8.5 6.5h12 M8.5 12h12 M8.5 17.5h12 M4 6.5h.01 M4 12h.01 M4 17.5h.01"),
    };

    public static IconDefinition? Get(IconKind kind) => Definitions.GetValueOrDefault(kind);

    private static IconDefinition Define(string? fill, string stroke) =>
        new(fill is null ? null : Parse(fill), Parse(stroke));

    private static Geometry Parse(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
