using System.Windows;
using System.Windows.Media;
using PackWise.Core.Files;

namespace PackWise.Icons;

/// <summary>
/// Colour-coded file and folder icons for lists. Each kind shares one page silhouette with a
/// distinct glyph and tint, so types are recognisable without relying on colour alone.
/// </summary>
public sealed class FileTypeIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(FileKind), typeof(FileTypeIcon),
        new FrameworkPropertyMetadata(FileKind.Generic, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsOverlayWarningProperty = DependencyProperty.Register(
        nameof(IsOverlayWarning), typeof(bool), typeof(FileTypeIcon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsOverlayLockProperty = DependencyProperty.Register(
        nameof(IsOverlayLock), typeof(bool), typeof(FileTypeIcon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Geometry Page = Freeze("M6.25 2.5h8.25l5 5v12.75a1.75 1.75 0 0 1-1.75 1.75H6.25A1.75 1.75 0 0 1 4.5 20.25V4.25A1.75 1.75 0 0 1 6.25 2.5z");
    private static readonly Geometry PageFold = Freeze("M14.5 2.5v3.75a1.25 1.25 0 0 0 1.25 1.25h3.75");
    private static readonly Geometry FolderBack = Freeze("M2.5 17.75V5.75A1.75 1.75 0 0 1 4.25 4h4.4a1.5 1.5 0 0 1 1.1.48L11.5 6.5h8.25a1.75 1.75 0 0 1 1.75 1.75v9.5A1.75 1.75 0 0 1 19.75 19.5H4.25A1.75 1.75 0 0 1 2.5 17.75z");
    private static readonly Geometry FolderFront = Freeze("M2.5 9.25h19v8.5a1.75 1.75 0 0 1-1.75 1.75H4.25A1.75 1.75 0 0 1 2.5 17.75z");
    private static readonly Geometry BadgeCircle = Freeze("M18.25 12.25a6 6 0 1 1 0 12 6 6 0 1 1 0-12z");
    private static readonly Geometry WarningMark = Freeze("M18.25 15v3.5 M18.25 21.25v.01");
    private static readonly Geometry LockBody = Freeze("M15.9 18h4.7a.8.8 0 0 1 .8.8v2.4a.8.8 0 0 1-.8.8h-4.7a.8.8 0 0 1-.8-.8v-2.4a.8.8 0 0 1 .8-.8z M16.75 18v-1.1a1.5 1.5 0 0 1 3 0V18");

    private static readonly Dictionary<FileKind, Geometry> Glyphs = new()
    {
        [FileKind.Archive] = Freeze("M12 6.5v1.25 M12 9.25v1.25 M10.5 12h3v3.25a1 1 0 0 1-1 1h-1a1 1 0 0 1-1-1z"),
        [FileKind.Image] = Freeze("M7.5 18.25l3-3.75 2.25 2.25 1.5-1.75 2.5 3.25z M14.75 10.5a1.25 1.25 0 1 1 0 2.5 1.25 1.25 0 1 1 0-2.5z"),
        [FileKind.Audio] = Freeze("M10.25 17.5a1.75 1.75 0 1 1-1.75-1.75h1.75V10.5l5-1.25v6.75 M15.25 16a1.75 1.75 0 1 1-1.75-1.75h1.75"),
        [FileKind.Video] = Freeze("M9.75 11v6.5l5.5-3.25z"),
        [FileKind.Document] = Freeze("M8 11.5h8 M8 14.25h8 M8 17h5"),
        [FileKind.Spreadsheet] = Freeze("M7.75 11h8.5v7h-8.5z M7.75 14.5h8.5 M12 11v7"),
        [FileKind.Presentation] = Freeze("M7.5 11h9v5.5h-9z M12 16.5v2 M10.25 18.5h3.5"),
        [FileKind.Pdf] = Freeze("M8 12.5c2.5 0 5-3 4-4.25s-1.5 3.75 1.25 6.5 3.75 1.5 3-.25-5.75 0-7.25 2.75"),
        [FileKind.Code] = Freeze("M10 11.25l-2.25 2.75 2.25 2.75 M14 11.25l2.25 2.75-2.25 2.75"),
        [FileKind.Text] = Freeze("M8 11.5h8 M8 14.25h6 M8 17h8"),
        [FileKind.Executable] = Freeze("M7.75 11h8.5v7h-8.5z M7.75 13h8.5"),
        [FileKind.Font] = Freeze("M9 18l3-7.5 3 7.5 M10.15 15.25h3.7"),
        [FileKind.DiskImage] = Freeze("M12 10.5a3.75 3.75 0 1 1 0 7.5 3.75 3.75 0 1 1 0-7.5z M12 13.75v.01"),
    };

    static FileTypeIcon()
    {
        WidthProperty.OverrideMetadata(typeof(FileTypeIcon), new FrameworkPropertyMetadata(16.0));
        HeightProperty.OverrideMetadata(typeof(FileTypeIcon), new FrameworkPropertyMetadata(16.0));
        FocusableProperty.OverrideMetadata(typeof(FileTypeIcon), new FrameworkPropertyMetadata(false));
    }

    public FileKind Kind
    {
        get => (FileKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public bool IsOverlayWarning
    {
        get => (bool)GetValue(IsOverlayWarningProperty);
        set => SetValue(IsOverlayWarningProperty, value);
    }

    public bool IsOverlayLock
    {
        get => (bool)GetValue(IsOverlayLockProperty);
        set => SetValue(IsOverlayLockProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var scale = size / 24.0;
        var color = KindColor(Kind);
        var stroke = new SolidColorBrush(color);
        var tint = new SolidColorBrush(Color.FromArgb(46, color.R, color.G, color.B));
        var strong = new SolidColorBrush(Color.FromArgb(110, color.R, color.G, color.B));
        var pen = MakePen(stroke, size <= 18 ? 1.75 : 1.5);

        drawingContext.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        if (Kind == FileKind.Folder)
        {
            drawingContext.DrawGeometry(strong, pen, FolderBack);
            drawingContext.DrawGeometry(tint, pen, FolderFront);
        }
        else
        {
            drawingContext.DrawGeometry(tint, pen, Page);
            drawingContext.DrawGeometry(null, pen, PageFold);
            if (Glyphs.TryGetValue(Kind, out var glyph))
            {
                drawingContext.DrawGeometry(Kind is FileKind.Video or FileKind.Image ? strong : null, pen, glyph);
            }
        }

        if (IsOverlayWarning || IsOverlayLock)
        {
            var badgeColor = IsOverlayWarning ? Color.FromRgb(0xE0, 0x7A, 0x00) : Color.FromRgb(0x3F, 0x4C, 0x5C);
            var ring = MakePen(TryFindResource("Brush.Surface") as Brush ?? Brushes.White, 2.2);
            drawingContext.DrawGeometry(new SolidColorBrush(badgeColor), ring, BadgeCircle);
            var mark = MakePen(Brushes.White, 2.1);
            drawingContext.DrawGeometry(IsOverlayWarning ? null : Brushes.White, IsOverlayWarning ? mark : MakePen(Brushes.White, 1.3), IsOverlayWarning ? WarningMark : LockBody);
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    public static Color KindColor(FileKind kind) => kind switch
    {
        FileKind.Folder => Color.FromRgb(0xE0, 0x9F, 0x1F),
        FileKind.Archive => Color.FromRgb(0x0F, 0x9A, 0x96),
        FileKind.Image => Color.FromRgb(0x8B, 0x5C, 0xF6),
        FileKind.Audio => Color.FromRgb(0xDB, 0x40, 0x8C),
        FileKind.Video => Color.FromRgb(0xE1, 0x4B, 0x4B),
        FileKind.Document => Color.FromRgb(0x2F, 0x76, 0xE8),
        FileKind.Spreadsheet => Color.FromRgb(0x18, 0x9C, 0x52),
        FileKind.Presentation => Color.FromRgb(0xEA, 0x6C, 0x1A),
        FileKind.Pdf => Color.FromRgb(0xD3, 0x2F, 0x2F),
        FileKind.Code => Color.FromRgb(0x0B, 0x93, 0xD1),
        FileKind.Text => Color.FromRgb(0x6B, 0x7A, 0x8C),
        FileKind.Executable => Color.FromRgb(0xC9, 0x7A, 0x0A),
        FileKind.Font => Color.FromRgb(0xA3, 0x4E, 0xE0),
        FileKind.DiskImage => Color.FromRgb(0x5B, 0x63, 0xE6),
        _ => Color.FromRgb(0x84, 0x93, 0xA6),
    };

    private static Pen MakePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    private static Geometry Freeze(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
