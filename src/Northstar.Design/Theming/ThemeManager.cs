using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Northstar.Design.Theming;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// An app's brand colour for each theme. Hover, pressed and tint shades are derived automatically,
/// so every Northstar Labs app gets the same interaction feel in its own colour.
/// </summary>
public sealed record BrandAccent(Color Light, Color Dark)
{
    /// <summary>PackWise teal, also the Northstar default.</summary>
    public static BrandAccent Teal { get; } = new(Color.FromRgb(0x0E, 0x8A, 0x87), Color.FromRgb(0x2F, 0xBF, 0xB8));
}

/// <summary>Applies the light or dark palette app-wide and keeps native title bars in step.</summary>
public static partial class ThemeManager
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    private static readonly Uri LightPalette = new("pack://application:,,,/Northstar.Design;component/Themes/Palette.Light.xaml");
    private static readonly Uri DarkPalette = new("pack://application:,,,/Northstar.Design;component/Themes/Palette.Dark.xaml");

    private static AppTheme _theme = AppTheme.System;
    private static BrandAccent _accent = BrandAccent.Teal;

    public static event EventHandler? ThemeChanged;

    public static bool IsDark { get; private set; }

    /// <summary>Loads the palette for <paramref name="theme"/> and follows Windows when set to System.</summary>
    public static void Initialize(AppTheme theme, BrandAccent? accent = null)
    {
        _theme = theme;
        _accent = accent ?? BrandAccent.Teal;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Apply();
    }

    public static void SetTheme(AppTheme theme)
    {
        _theme = theme;
        Apply();
    }

    public static void Shutdown() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    /// <summary>Colours the native caption of a window to match the palette.</summary>
    public static void ApplyWindowChrome(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        if (Application.Current.TryFindResource("Color.Chrome") is Color caption &&
            Application.Current.TryFindResource("Color.Text") is Color text &&
            Application.Current.TryFindResource("Color.Border") is Color border)
        {
            var captionRef = ToColorRef(caption);
            var textRef = ToColorRef(text);
            var borderRef = ToColorRef(border);
            _ = DwmSetWindowAttribute(handle, DwmCaptionColor, ref captionRef, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmTextColor, ref textRef, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmBorderColor, ref borderRef, sizeof(int));
        }
    }

    private static void Apply()
    {
        IsDark = _theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemPrefersDark(),
        };

        var resources = Application.Current.Resources;
        var dictionaries = resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = IsDark ? DarkPalette : LightPalette };
        var existing = dictionaries.FirstOrDefault(IsPalette);
        if (existing is null)
        {
            dictionaries.Insert(0, palette);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = palette;
        }

        ApplyAccent(resources);

        foreach (Window window in Application.Current.Windows)
        {
            ApplyWindowChrome(window);
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Top-level resources win over merged palettes, so the accent overrides cleanly.</summary>
    private static void ApplyAccent(ResourceDictionary resources)
    {
        var accent = IsDark ? _accent.Dark : _accent.Light;
        var surface = IsDark ? Color.FromRgb(0x1A, 0x1F, 0x25) : Colors.White;

        Set(resources, "Accent", accent);
        Set(resources, "IconAccent", accent);
        Set(resources, "AccentHover", IsDark ? Mix(accent, Colors.White, 0.12) : Mix(accent, Colors.Black, 0.12));
        Set(resources, "AccentPressed", IsDark ? Mix(accent, Colors.Black, 0.12) : Mix(accent, Colors.Black, 0.25));
        Set(resources, "AccentSoft", Mix(surface, accent, IsDark ? 0.22 : 0.17));
        Set(resources, "AccentSoftHover", Mix(surface, accent, IsDark ? 0.12 : 0.10));
        Set(resources, "AccentText", IsDark ? Mix(accent, Colors.White, 0.2) : Mix(accent, Colors.Black, 0.2));
    }

    private static void Set(ResourceDictionary resources, string name, Color color)
    {
        resources[$"Color.{name}"] = color;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[$"Brush.{name}"] = brush;
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + ((to.R - from.R) * amount)),
        (byte)(from.G + ((to.G - from.G) * amount)),
        (byte)(from.B + ((to.B - from.B) * amount)));

    private static bool IsPalette(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString is { } source &&
        (source.EndsWith("Palette.Light.xaml", StringComparison.OrdinalIgnoreCase) || source.EndsWith("Palette.Dark.xaml", StringComparison.OrdinalIgnoreCase));

    private static bool SystemPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_theme == AppTheme.System && e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Application.Current.Dispatcher.BeginInvoke(Apply);
        }
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
