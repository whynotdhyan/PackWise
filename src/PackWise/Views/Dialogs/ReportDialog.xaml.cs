using System.Text;
using System.Windows;
using PackWise.Infrastructure;

namespace PackWise.Views.Dialogs;

public sealed record ReportLine(string Item, string Message);

public sealed record Report
{
    public required string Title { get; init; }

    public required string Summary { get; init; }

    public MessageKind Kind { get; init; } = MessageKind.Success;

    public IReadOnlyList<ReportLine> Lines { get; init; } = [];

    public string? ActionText { get; init; }

    public Action? Action { get; init; }
}

public partial class ReportDialog
{
    private readonly Report _report;

    private ReportDialog(Report report)
    {
        InitializeComponent();
        _report = report;
        Title = report.Title;
        Heading.Text = report.Title;
        SummaryText.Text = report.Summary;

        if (report.Lines.Count > 0)
        {
            ProblemList.ItemsSource = report.Lines;
            ProblemList.Visibility = Visibility.Visible;
            CopyButton.Visibility = Visibility.Visible;
        }

        if (report.ActionText is not null && report.Action is not null)
        {
            ActionButton.Content = report.ActionText;
            ActionButton.Visibility = Visibility.Visible;
        }

        var (icon, brush, soft) = report.Kind switch
        {
            MessageKind.Warning => (IconKind.Warning, "Brush.Warning", "Brush.WarningSoft"),
            MessageKind.Error => (IconKind.Error, "Brush.Danger", "Brush.DangerSoft"),
            MessageKind.Info => (IconKind.Info, "Brush.Info", "Brush.InfoSoft"),
            _ => (IconKind.Success, "Brush.Success", "Brush.SuccessSoft"),
        };
        Glyph.Kind = icon;
        Glyph.SetResourceReference(GlyphIcon.ForegroundProperty, brush);
        Badge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, soft);
    }

    public static void Show(Window owner, Report report)
    {
        var dialog = new ReportDialog(report) { Owner = owner };
        dialog.ShowDialog();
    }

    private void OnAction(object sender, RoutedEventArgs e)
    {
        Close();
        _report.Action?.Invoke();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var text = new StringBuilder().AppendLine(_report.Title).AppendLine(_report.Summary).AppendLine();
        foreach (var line in _report.Lines)
        {
            text.Append(line.Item).Append('\t').AppendLine(line.Message);
        }

        Shell.CopyText(text.ToString());
    }
}
