using System.Windows;
using PackWise.Core.Files;

namespace PackWise.Views.Dialogs;

public sealed record InfoRow(string Label, string Value, bool IsWarning = false, bool IsGood = false);

public sealed record InfoSection(string Title, IReadOnlyList<InfoRow> Rows);

public partial class InfoDialog
{
    private readonly Action? _action;

    private InfoDialog(string heading, string subheading, FileKind kind, IReadOnlyList<InfoSection> sections, string? actionText, Action? action)
    {
        InitializeComponent();
        Heading.Text = heading;
        SubHeading.Text = subheading;
        SubHeading.ToolTip = subheading;
        HeaderIcon.Kind = kind;
        Sections.ItemsSource = sections.Where(s => s.Rows.Count > 0).ToList();
        _action = action;
        if (actionText is not null && action is not null)
        {
            ActionButton.Content = actionText;
            ActionButton.Visibility = Visibility.Visible;
        }
    }

    public static void Show(Window owner, string heading, string subheading, FileKind kind, IReadOnlyList<InfoSection> sections, string? actionText = null, Action? action = null)
    {
        var dialog = new InfoDialog(heading, subheading, kind, sections, actionText, action) { Owner = owner };
        dialog.ShowDialog();
    }

    private void OnAction(object sender, RoutedEventArgs e)
    {
        Close();
        _action?.Invoke();
    }
}
