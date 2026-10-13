using System.Windows;
using System.Windows.Controls;
using PackWise.Archiving;

namespace PackWise.Views.Dialogs;

public partial class PasswordDialog
{
    private bool _syncing;

    private PasswordDialog(string archivePath, bool retry)
    {
        InitializeComponent();
        ArchiveName.Text = Path.GetFileName(archivePath);
        ArchiveName.ToolTip = archivePath;
        RetryBanner.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => Secret.Focus();
    }

    private string Password => Secret.Password;

    public static string? Ask(Window? owner, string archivePath, bool retry)
    {
        var dialog = new PasswordDialog(archivePath, retry);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog.Password : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = Password.Length > 0;

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        OkButton.IsEnabled = Secret.Password.Length > 0;
        if (!_syncing)
        {
            _syncing = true;
            Visible.Text = Secret.Password;
            _syncing = false;
        }
    }

    private void OnVisibleChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing)
        {
            _syncing = true;
            Secret.Password = Visible.Text;
            _syncing = false;
        }
    }

    private void OnRevealChanged(object sender, RoutedEventArgs e)
    {
        var show = Reveal.IsChecked == true;
        Visible.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Secret.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        RevealIcon.Kind = show ? IconKind.EyeOff : IconKind.Eye;
        Reveal.ToolTip = show ? "Hide password" : "Show password";
        (show ? (Control)Visible : Secret).Focus();
    }
}

/// <summary>Asks the user for archive passwords from engine worker threads.</summary>
public sealed class UiPasswordProvider : IPasswordProvider
{
    private readonly Func<Window?> _owner;

    public UiPasswordProvider(Func<Window?> owner) => _owner = owner;

    public string? GetPassword(string archivePath, bool previousAttemptFailed) =>
        Application.Current.Dispatcher.Invoke(() => PasswordDialog.Ask(_owner(), archivePath, previousAttemptFailed));
}
