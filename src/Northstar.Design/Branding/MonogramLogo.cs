using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Northstar.Design.Branding;

public enum MonogramMark
{
    /// <summary>Four-point north star — the Northstar Labs signature.</summary>
    Star,

    /// <summary>Small rounded square, e.g. the "packed box" in the PackWise P.</summary>
    Square,
}

/// <summary>
/// A mark placed relative to one letter's ink bounds. X and Y are fractions of that letter's
/// width and height (0,0 = top-left); Size is a fraction of the tile.
/// </summary>
public sealed record MonogramDecoration(MonogramMark Mark, int LetterIndex, double X, double Y, double Size, Color Color);

/// <summary>
/// The Northstar Labs app-icon system: a rounded dark tile, an inset outline and a bold two-letter
/// monogram, with an optional signature mark. Every product uses the same geometry and only
/// changes its letters and colours, so the family reads as one.
/// </summary>
public sealed record MonogramLogo(
    string Letters,
    Color Background,
    Color BackgroundEdge,
    Color Outline,
    Color Foreground,
    IReadOnlyList<MonogramDecoration> Decorations)
{
    public const double Size = 64;

    private const double TileInset = 1;
    private const double TileRadius = 12.5;
    private const double OutlineInset = 3.7;
    private const double OutlineRadius = 9;
    private const double OutlineThickness = 0.9;
    private const double LetterHeight = 21;
    private const double MaxLetterWidth = 41;

    /// <summary>PackWise: teal "Pw" whose P bowl holds a small packed box.</summary>
    public static MonogramLogo PackWise { get; } = new(
        "Pw",
        Color.FromRgb(0x06, 0x22, 0x21),
        Color.FromRgb(0x05, 0x1D, 0x1C),
        Color.FromRgb(0x2E, 0xE6, 0xC8),
        Color.FromRgb(0x2E, 0xE6, 0xC8),
        [new MonogramDecoration(MonogramMark.Square, 0, 0.47, 0.29, 0.055, Color.FromRgb(0xE8, 0xFF, 0xFA))]);

    /// <summary>Northstar Labs: cream "Nı" with a teal north star as the dot of the i.</summary>
    public static MonogramLogo Northstar { get; } = new(
        "Nı",
        Color.FromRgb(0x0B, 0x14, 0x33),
        Color.FromRgb(0x07, 0x0D, 0x24),
        Color.FromRgb(0xF1, 0xEC, 0xE2),
        Color.FromRgb(0xF1, 0xEC, 0xE2),
        [new MonogramDecoration(MonogramMark.Star, 1, 0.5, -0.36, 0.20, Color.FromRgb(0x2E, 0xE6, 0xC8))]);

    /// <summary>Builds the logo as frozen vector artwork on a 64×64 canvas.</summary>
    public DrawingGroup CreateDrawing(double pixelsPerDip = 1.0)
    {
        var group = new DrawingGroup();

        var tile = new RectangleGeometry(new Rect(TileInset, TileInset, Size - (2 * TileInset), Size - (2 * TileInset)), TileRadius, TileRadius);
        var fill = new LinearGradientBrush(Background, BackgroundEdge, new Point(0.2, 0), new Point(0.8, 1));
        group.Children.Add(new GeometryDrawing(fill, null, tile));

        var outline = new RectangleGeometry(new Rect(OutlineInset, OutlineInset, Size - (2 * OutlineInset), Size - (2 * OutlineInset)), OutlineRadius, OutlineRadius);
        group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Outline), OutlineThickness), outline));

        var letters = BuildLetters(pixelsPerDip, out var letterBounds);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Foreground), null, letters));

        foreach (var decoration in Decorations)
        {
            if (decoration.LetterIndex < letterBounds.Count)
            {
                group.Children.Add(Decorate(decoration, letterBounds[decoration.LetterIndex]));
            }
        }

        group.Freeze();
        return group;
    }

    private Geometry BuildLetters(double pixelsPerDip, out List<Rect> bounds)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var text = new FormattedText(Letters, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 100, Brushes.Black, pixelsPerDip);
        var geometry = text.BuildGeometry(new Point(0, 0));
        var ink = geometry.Bounds;

        // Scale so the cap height lands on LetterHeight, then centre optically (slightly low).
        var scale = Math.Min(LetterHeight / ink.Height, MaxLetterWidth / ink.Width);
        var width = ink.Width * scale;
        var height = ink.Height * scale;
        var offsetX = ((Size - width) / 2) - (ink.Left * scale);
        var offsetY = ((Size - height) / 2) + 0.5 - (ink.Top * scale);
        var transform = new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY);

        bounds = [];
        for (var i = 0; i < Letters.Length; i++)
        {
            var single = new FormattedText(Letters[i].ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 100, Brushes.Black, pixelsPerDip);
            var start = text.BuildHighlightGeometry(new Point(0, 0), i, 1)?.Bounds.Left ?? 0;
            var letterInk = single.BuildGeometry(new Point(start, 0)).Bounds;
            bounds.Add(transform.TransformBounds(letterInk));
        }

        var placed = geometry.Clone();
        placed.Transform = transform;
        return placed;
    }

    private static GeometryDrawing Decorate(MonogramDecoration decoration, Rect letter)
    {
        var size = decoration.Size * Size;
        var center = new Point(letter.Left + (letter.Width * decoration.X), letter.Top + (letter.Height * decoration.Y));
        var brush = new SolidColorBrush(decoration.Color);

        Geometry shape = decoration.Mark switch
        {
            MonogramMark.Star => Star(center, size / 2, size * 0.13),
            _ => new RectangleGeometry(new Rect(center.X - (size / 2), center.Y - (size / 2), size, size), size * 0.22, size * 0.22),
        };

        return new GeometryDrawing(brush, null, shape);
    }

    private static StreamGeometry Star(Point center, double outer, double inner)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < 8; i++)
            {
                var radius = i % 2 == 0 ? outer : inner;
                var angle = (-Math.PI / 2) + (i * Math.PI / 4);
                var point = new Point(center.X + (radius * Math.Cos(angle)), center.Y + (radius * Math.Sin(angle)));
                if (i == 0)
                {
                    context.BeginFigure(point, isFilled: true, isClosed: true);
                }
                else
                {
                    context.LineTo(point, isStroked: false, isSmoothJoin: false);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }
}

/// <summary>Displays a <see cref="MonogramLogo"/> at any size.</summary>
public sealed class BrandTile : FrameworkElement
{
    public static readonly DependencyProperty LogoProperty = DependencyProperty.Register(
        nameof(Logo), typeof(MonogramLogo), typeof(BrandTile),
        new FrameworkPropertyMetadata(MonogramLogo.PackWise, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((BrandTile)d)._drawing = null));

    private DrawingGroup? _drawing;

    public MonogramLogo Logo
    {
        get => (MonogramLogo)GetValue(LogoProperty);
        set => SetValue(LogoProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        _drawing ??= Logo.CreateDrawing(VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var scale = size / MonogramLogo.Size;
        drawingContext.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        drawingContext.DrawDrawing(_drawing);
        drawingContext.Pop();
        drawingContext.Pop();
    }
}
