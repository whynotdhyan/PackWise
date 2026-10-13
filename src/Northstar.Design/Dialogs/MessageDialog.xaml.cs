using System.Windows;
using Northstar.Design.Icons;

namespace Northstar.Design.Dialogs;

public enum MessageKind
{
    Info,
    Success,
    Warning,
    Error,
    Question,
}

public enum MessageResult
{
    None,
    Primary,
    Secondary,
    Tertiary,
}

public sealed record MessageOptions
{
    public MessageKind Kind { get; init; } = MessageKind.Info;

    public required string Title { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Details { get; init; }

    public string Primary { get; init; } = "OK";

    public string? Secondary { get; init; }

    public string? Tertiary { get; init; }

    public string? OptionText { get; init; }

    public bool DangerousPrimary { get; init; }
}

public partial class MessageDialog
{
    private MessageResult _result = MessageResult.None;

    private MessageDialog(MessageOptions options)
    {
        InitializeComponent();
        Heading.Text = options.Title;
        Body.Text = options.Message;
        Body.Visibility = string.IsNullOrEmpty(options.Message) ? Visibility.Collapsed : Visibility.Visible;

        if (!string.IsNullOrEmpty(options.Details))
        {
            Details.Text = options.Details;
            Details.Visibility = Visibility.Visible;
        }

        if (options.OptionText is not null)
        {
            Option.Content = options.OptionText;
            Option.Visibility = Visibility.Visible;
        }

        PrimaryButton.Content = options.Primary;
        if (options.DangerousPrimary)
        {
            PrimaryButton.SetResourceReference(StyleProperty, "Button.Danger");
        }

        Configure(SecondaryButton, options.Secondary);
        Configure(TertiaryButton, options.Tertiary);

        var (icon, brush, soft) = options.Kind switch
        {
            MessageKind.Success => (IconKind.Success, "Brush.Success", "Brush.SuccessSoft"),
            MessageKind.Warning => (IconKind.Warning, "Brush.Warning", "Brush.WarningSoft"),
            MessageKind.Error => (IconKind.Error, "Brush.Danger", "Brush.DangerSoft"),
            MessageKind.Question => (IconKind.Help, "Brush.Accent", "Brush.AccentSoft"),
            _ => (IconKind.Info, "Brush.Info", "Brush.InfoSoft"),
        };
        Glyph.Kind = icon;
        Glyph.SetResourceReference(GlyphIcon.ForegroundProperty, brush);
        IconBadge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, soft);
    }

    public bool OptionChecked => Option.IsChecked == true;

    public static MessageResult Show(Window? owner, MessageOptions options) => Show(owner, options, out _);

    public static MessageResult Show(Window? owner, MessageOptions options, out bool optionChecked)
    {
        var dialog = new MessageDialog(options);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        optionChecked = dialog.OptionChecked;
        return dialog._result;
    }

    public static void Error(Window? owner, string title, string message, string? details = null) =>
        Show(owner, new MessageOptions { Kind = MessageKind.Error, Title = title, Message = message, Details = details });

    public static void Info(Window? owner, string title, string message) =>
        Show(owner, new MessageOptions { Kind = MessageKind.Info, Title = title, Message = message });

    public static bool Confirm(Window? owner, string title, string message, string confirm, MessageKind kind = MessageKind.Question, bool dangerous = false) =>
        Show(owner, new MessageOptions
        {
            Kind = kind,
            Title = title,
            Message = message,
            Primary = confirm,
            Secondary = "Cancel",
            DangerousPrimary = dangerous,
        }) == MessageResult.Primary;

    private static void Configure(System.Windows.Controls.Button button, string? text)
    {
        if (text is not null)
        {
            button.Content = text;
            button.Visibility = Visibility.Visible;
        }
    }

    private void OnPrimary(object sender, RoutedEventArgs e) => Finish(MessageResult.Primary);

    private void OnSecondary(object sender, RoutedEventArgs e) => Finish(MessageResult.Secondary);

    private void OnTertiary(object sender, RoutedEventArgs e) => Finish(MessageResult.Tertiary);

    private void Finish(MessageResult result)
    {
        _result = result;
        Close();
    }
}
