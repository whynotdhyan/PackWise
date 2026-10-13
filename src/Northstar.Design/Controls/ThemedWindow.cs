using System.Windows;
using System.Windows.Input;

using Northstar.Design.Theming;

namespace Northstar.Design.Controls;

/// <summary>Base for every Northstar Labs window: themed caption, Escape-to-cancel for dialogs.</summary>
public class ThemedWindow : Window
{
    public static readonly DependencyProperty IsDialogProperty = DependencyProperty.Register(
        nameof(IsDialog), typeof(bool), typeof(ThemedWindow), new PropertyMetadata(false, OnIsDialogChanged));

    public ThemedWindow()
    {
        SetResourceReference(StyleProperty, "Style.Window");
        if (Application.Current?.MainWindow is { IsLoaded: true } main && !ReferenceEquals(main, this))
        {
            Owner = main;
        }
    }

    public bool IsDialog
    {
        get => (bool)GetValue(IsDialogProperty);
        set => SetValue(IsDialogProperty, value);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.ApplyWindowChrome(this);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && IsDialog && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private static void OnIsDialogChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ThemedWindow window && (bool)e.NewValue)
        {
            window.SetResourceReference(StyleProperty, "Style.Dialog");
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }
}
