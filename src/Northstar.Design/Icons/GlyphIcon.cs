using System.Windows;
using System.Windows.Media;

namespace Northstar.Design.Icons;

/// <summary>Renders an <see cref="IconKind"/> glyph at any size, following the current theme brushes.</summary>
public sealed class GlyphIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(IconKind), typeof(GlyphIcon),
        new FrameworkPropertyMetadata(IconKind.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(GlyphIcon),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(GlyphIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentOpacityProperty = DependencyProperty.Register(
        nameof(AccentOpacity), typeof(double), typeof(GlyphIcon),
        new FrameworkPropertyMetadata(0.28, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(GlyphIcon),
        new FrameworkPropertyMetadata(1.6, FrameworkPropertyMetadataOptions.AffectsRender));

    static GlyphIcon()
    {
        WidthProperty.OverrideMetadata(typeof(GlyphIcon), new FrameworkPropertyMetadata(16.0));
        HeightProperty.OverrideMetadata(typeof(GlyphIcon), new FrameworkPropertyMetadata(16.0));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(GlyphIcon), new FrameworkPropertyMetadata(true));
        FocusableProperty.OverrideMetadata(typeof(GlyphIcon), new FrameworkPropertyMetadata(false));
    }

    public IconKind Kind
    {
        get => (IconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush? Accent
    {
        get => (Brush?)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public double AccentOpacity
    {
        get => (double)GetValue(AccentOpacityProperty);
        set => SetValue(AccentOpacityProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (IconLibrary.Get(Kind) is not { } icon || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var scale = Math.Min(ActualWidth, ActualHeight) / 24.0;
        drawingContext.PushTransform(new TranslateTransform((ActualWidth - (24 * scale)) / 2, (ActualHeight - (24 * scale)) / 2));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        if (icon.Fill is not null)
        {
            drawingContext.PushOpacity(AccentOpacity);
            drawingContext.DrawGeometry(Accent ?? Foreground, null, icon.Fill);
            drawingContext.Pop();
        }

        // Keep strokes visually consistent: slightly heavier at small sizes, lighter when large.
        var thickness = StrokeThickness * Math.Clamp(1.15 - ((scale - 0.66) * 0.18), 0.85, 1.15);
        var pen = new Pen(Foreground, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        drawingContext.DrawGeometry(null, pen, icon.Stroke);

        drawingContext.Pop();
        drawingContext.Pop();
    }
}
