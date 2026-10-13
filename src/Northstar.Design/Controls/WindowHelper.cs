using System.Windows;

namespace Northstar.Design.Controls;

public static class WindowHelper
{
    /// <summary>The window prompts should belong to: the active app window, else the main window.</summary>
    public static Window? ActiveOrMain()
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        return app.Windows.OfType<Window>().LastOrDefault(w => w.IsActive && w.IsVisible) ?? app.MainWindow;
    }
}
