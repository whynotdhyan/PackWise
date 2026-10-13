using System.Diagnostics;
using System.Reflection;
using System.Windows;
using PackWise.Services;
using PackWise.ViewModels;
using PackWise.Views.Tools;

namespace PackWise.Integration;

/// <summary>
/// Runs Explorer right-click commands without the main window: only the dialogs and progress the
/// task needs appear, centred on screen, and the process exits when the work is done.
/// </summary>
public static class CommandRunner
{
    public static string Version =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0.0";

    /// <summary>Returns true when the request was handled here and the app should not open its main window.</summary>
    public static async Task<bool> TryRunAsync(CommandRequest request, AppServices services)
    {
        switch (request.Verb)
        {
            case "open":
                return false;

            case "integrate":
                Integrate(request.Options);
                return true;

            case "uninstall":
                Uninstall(request.Options.Contains("quiet"), request.Options.Contains("keep-settings"), services);
                return true;

            case "checksum":
                await ShowToolAsync(new ChecksumWindow(new ChecksumViewModel(await CollectAsync(request) ?? [])));
                return true;

            case "duplicates":
                await ShowToolAsync(new DuplicateFinderWindow(new DuplicateFinderViewModel(services, request.Paths)));
                return true;
        }

        if (!request.IsBatch)
        {
            return false;
        }

        var paths = await CollectAsync(request);
        if (paths is null || paths.Count == 0)
        {
            return true;
        }

        var host = new CommandHostWindow();
        host.Show();
        using var model = new MainViewModel(host, services);
        try
        {
            var archives = paths.Where(File.Exists).ToList();
            switch (request.Verb)
            {
                case "add":
                    await model.CreateArchiveAsync(paths, Path.GetDirectoryName(paths[0]) ?? paths[0]);
                    break;
                case "zip":
                    await model.QuickArchiveAsync(paths, "zip");
                    break;
                case "7z":
                    await model.QuickArchiveAsync(paths, "7z");
                    break;
                case "extract-here":
                    await model.ExtractFilesAsync(archives, ExtractMode.Here);
                    break;
                case "extract-folder":
                    await model.ExtractFilesAsync(archives, ExtractMode.ToNamedFolder);
                    break;
                case "extract-to":
                    await model.ExtractFilesAsync(archives, ExtractMode.Dialog);
                    break;
                case "test":
                    await model.TestFilesAsync(archives);
                    break;
                case "convert-zip":
                    await model.ConvertAsync(archives, "zip");
                    break;
                case "convert-7z":
                    await model.ConvertAsync(archives, "7z");
                    break;
            }

            ShowFailureIfAny(model);
        }
        finally
        {
            host.Close();
        }

        return true;
    }

    private static Task<IReadOnlyList<string>?> CollectAsync(CommandRequest request) =>
        request.Paths.Count == 0 ? Task.FromResult<IReadOnlyList<string>?>([]) : SelectionCollector.CollectAsync(request);

    private static async Task ShowToolAsync(Window window)
    {
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
        await closed.Task;
    }

    /// <summary>Banners are invisible without the main window, so warnings surface as dialogs.</summary>
    private static void ShowFailureIfAny(MainViewModel model)
    {
        if (model.Banner is { Kind: NoticeKind.Warning or NoticeKind.Danger } notice)
        {
            MessageDialog.Show(null, new MessageOptions { Kind = MessageKind.Warning, Title = "PackWise", Message = notice.Text });
        }
    }

    // ---------------------------------------------------------------- Install and uninstall

    /// <summary>Called by the installer: "menu", "associations", "startmenu", "desktop".</summary>
    private static void Integrate(IReadOnlySet<string> options)
    {
        if (options.Contains("menu"))
        {
            ShellIntegration.RegisterContextMenu();
        }

        if (options.Contains("associations"))
        {
            ShellIntegration.RegisterAssociations();
        }

        if (options.Contains("startmenu"))
        {
            ShellIntegration.CreateShortcut(ShellIntegration.StartMenuShortcut);
        }

        if (options.Contains("desktop"))
        {
            ShellIntegration.CreateShortcut(ShellIntegration.DesktopShortcut);
        }

        ShellIntegration.RegisterUninstallEntry(Version);
    }

    private static void Uninstall(bool quiet, bool keepSettings, AppServices services)
    {
        var removeSettings = !keepSettings;
        if (!quiet)
        {
            var answer = MessageDialog.Show(null, new MessageOptions
            {
                Kind = MessageKind.Question,
                Title = "Uninstall PackWise?",
                Message = "PackWise, its right-click menu, shortcuts and file associations will be removed. Your files and archives are not touched.",
                Primary = "Uninstall",
                Secondary = "Cancel",
                OptionText = "Also remove my settings and logs",
                DangerousPrimary = true,
            }, out removeSettings);

            if (answer != MessageResult.Primary)
            {
                return;
            }
        }

        ShellIntegration.RemoveAll();
        services.Log.Info("uninstall", "PackWise was uninstalled.");

        var folders = new List<string>();
        var installDirectory = ShellIntegration.InstallDirectory;
        if (installDirectory.StartsWith(ShellIntegration.DefaultInstallDirectory, StringComparison.OrdinalIgnoreCase))
        {
            folders.Add(installDirectory);
        }

        if (removeSettings)
        {
            folders.Add(services.Paths.Root);
        }

        // The running executable can't delete itself; a short-lived hidden shell finishes the job.
        if (folders.Count > 0)
        {
            var commands = string.Join(" & ", folders.Select(f => $"rmdir /s /q \"{f}\""));
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & {commands}")
            {
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
            })?.Dispose();
        }

        if (!quiet)
        {
            MessageDialog.Show(null, new MessageOptions { Kind = MessageKind.Success, Title = "PackWise was uninstalled", Message = "Thanks for using PackWise." });
        }
    }
}

/// <summary>An invisible owner that keeps right-click dialogs centred and in the taskbar.</summary>
internal sealed class CommandHostWindow : Window
{
    public CommandHostWindow()
    {
        Title = "PackWise";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = true;
        Width = 1;
        Height = 1;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/PackWise;component/Assets/PackWise.ico"));
    }
}
