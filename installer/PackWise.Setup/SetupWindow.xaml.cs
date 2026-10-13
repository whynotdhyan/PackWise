using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PackWise.Setup;

public partial class SetupWindow
{
    private readonly InstallOptions _options = new InstallOptions();
    private bool _installing;

    public SetupWindow()
    {
        InitializeComponent();
        VersionText.Text = "Version " + Installer.Version;

        var existing = Installer.ExistingDirectory;
        if (existing != null)
        {
            _options.Directory = existing;
            HeadingText.Text = "Update PackWise";
            var installed = Installer.ExistingVersion ?? string.Empty;
            IntroText.Text = installed == Installer.Version
                ? "PackWise " + installed + " is already installed. Setup will repair it and keep your settings."
                : "PackWise " + installed + " is installed. Setup will update it to " + Installer.Version + " and keep your settings.";
            InstallButton.Content = "Update";
        }

        LocationText.Text = _options.Directory;
        LocationText.ToolTip = _options.Directory;

        if (!Installer.HasPayload)
        {
            ShowError("This copy of Setup is incomplete. Download PackWise again from the official release page.");
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = _installing;
        base.OnClosing(e);
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        var running = Installer.RunningInstances(_options.Directory);
        if (running.Length > 0)
        {
            var answer = MessageBox.Show(this, "PackWise is running. Close it and continue?", "PackWise Setup", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (answer != MessageBoxResult.OK)
            {
                return;
            }

            foreach (var process in running)
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(4000))
                {
                    process.Kill();
                }
            }
        }

        _options.ContextMenu = MenuOption.IsChecked == true;
        _options.Associations = AssociationOption.IsChecked == true;
        _options.DesktopShortcut = DesktopOption.IsChecked == true;

        OptionsPage.Visibility = Visibility.Collapsed;
        ProgressPage.Visibility = Visibility.Visible;
        _installing = true;

        var progress = new Progress<double>(value =>
        {
            Progress.Value = value;
            ProgressText.Text = value < 0.9 ? "Copying files" : "Registering with Windows";
        });

        try
        {
            await Task.Run(() => Installer.Install(_options, progress, CancellationToken.None));
            ProgressPage.Visibility = Visibility.Collapsed;
            DonePage.Visibility = Visibility.Visible;
            DoneHeading.Text = InstallButton.Content as string == "Update" ? "PackWise is up to date" : "PackWise is ready";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is InvalidDataException)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _installing = false;
        }
    }

    private void ShowError(string message)
    {
        OptionsPage.Visibility = Visibility.Collapsed;
        ProgressPage.Visibility = Visibility.Collapsed;
        ErrorPage.Visibility = Visibility.Visible;
        ErrorText.Text = message;
    }

    private void OnFinish(object sender, RoutedEventArgs e)
    {
        if (LaunchOption.IsChecked == true)
        {
            Installer.Launch(_options.Directory);
        }

        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnLicense(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "PackWise is free software released under the MIT License by Northstar Labs.\n\n" +
            "It includes the 7-Zip engine by Igor Pavlov (GNU LGPL with the unRAR restriction).\n\n" +
            "Full license texts are installed in the licenses folder.",
            "PackWise license", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
