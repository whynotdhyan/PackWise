using System.Windows;
using System.Windows.Threading;
using PackWise.Integration;
using PackWise.Services;
using PackWise.Views;

namespace PackWise;

public partial class App
{
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _services?.Log.Error("app", $"Unobserved task error: {args.Exception.GetBaseException().Message}");
            args.SetObserved();
        };

        _services = AppServices.Initialize();
        ThemeManager.Initialize((AppTheme)_services.Settings.Theme, BrandAccent.Teal);

        try
        {
            // Load the engine early so a missing or broken 7z.dll is reported clearly at startup.
            _ = Archiving.ArchiveReader.EngineVersion;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            MessageDialog.Error(null, "The archive engine is missing",
                "PackWise couldn't load 7z.dll. Reinstall PackWise, or run eng/Get-SevenZip.ps1 when building from source.", ex.Message);
            Shutdown(1);
            return;
        }

        _ = StartAsync(CommandRequest.Parse(e.Args));
    }

    private async Task StartAsync(CommandRequest request)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (await CommandRunner.TryRunAsync(request, _services!))
        {
            _services!.SaveSettings();
            Shutdown();
            return;
        }

        var window = new MainWindow(_services!, request.Paths.FirstOrDefault());
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Shutdown();
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var error = e.Exception.GetBaseException();
        _services?.Log.Error("app", e.Exception.ToString());
        MessageDialog.Error(MainWindow, "Something went wrong",
            "PackWise hit an unexpected problem. Your files were not changed by this error.", error.ToString());
        e.Handled = true;
    }
}
