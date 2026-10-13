using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PackWise.Archiving;
using PackWise.Core.Extraction;
using PackWise.Core.Security;
using PackWise.Core.Settings;
using PackWise.Infrastructure;
using PackWise.Integration;
using PackWise.Services;

namespace PackWise.ViewModels;

/// <summary>Edits a copy of the settings; nothing changes until the dialog is confirmed.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        var s = services.Settings;
        Theme = s.Theme;
        ShowHiddenFiles = s.ShowHiddenFiles;
        ShowPreviews = s.ShowPreviews;
        MarkOfTheWeb = s.MarkOfTheWeb;
        ConflictPolicy = s.ConflictPolicy;
        ExtractToNewFolder = s.ExtractToNewFolder;
        OpenFolderAfterExtract = s.OpenFolderAfterExtract;
        KeepDamagedFiles = s.KeepDamagedFiles;
        RecycleReplacedFiles = s.RecycleReplacedFiles;
        WarnBeforeOpeningRiskyFiles = s.WarnBeforeOpeningRiskyFiles;
        DefaultFormat = s.DefaultFormat;
        DefaultLevel = s.DefaultLevel;
        TestAfterCreate = s.TestAfterCreate;
        AllowUntrustedFormats = s.AllowUntrustedFormats;
        LoggingEnabled = s.LoggingEnabled;
        LogRetentionDays = s.LogRetentionDays;
        ContextMenuEnabled = ShellIntegration.IsContextMenuRegistered();
        AssociationsEnabled = ShellIntegration.AreAssociationsRegistered();
        DesktopShortcut = File.Exists(ShellIntegration.DesktopShortcut);
    }

    [ObservableProperty]
    public partial bool ContextMenuEnabled { get; set; }

    [ObservableProperty]
    public partial bool AssociationsEnabled { get; set; }

    [ObservableProperty]
    public partial bool DesktopShortcut { get; set; }

    [RelayCommand]
    private static void OpenDefaultApps() => Shell.Open("ms-settings:defaultapps");

    public IReadOnlyList<ThemePreference> Themes { get; } = Enum.GetValues<ThemePreference>();

    public IReadOnlyList<LevelOption> Levels => AddArchiveViewModel.AllLevels;

    public IReadOnlyList<ArchiveFormat> UntrustedFormats { get; } =
        ArchiveReader.Formats.Where(f => !f.IsTrusted).OrderBy(f => f.Category).ThenBy(f => f.DisplayName).ToList();

    public string SettingsLocation => _services.Paths.Root;

    [ObservableProperty]
    public partial ThemePreference Theme { get; set; }

    [ObservableProperty]
    public partial bool ShowHiddenFiles { get; set; }

    [ObservableProperty]
    public partial bool ShowPreviews { get; set; }

    [ObservableProperty]
    public partial MarkOfTheWebPolicy MarkOfTheWeb { get; set; }

    [ObservableProperty]
    public partial ConflictPolicy ConflictPolicy { get; set; }

    [ObservableProperty]
    public partial bool ExtractToNewFolder { get; set; }

    [ObservableProperty]
    public partial bool OpenFolderAfterExtract { get; set; }

    [ObservableProperty]
    public partial bool KeepDamagedFiles { get; set; }

    [ObservableProperty]
    public partial bool RecycleReplacedFiles { get; set; }

    [ObservableProperty]
    public partial bool WarnBeforeOpeningRiskyFiles { get; set; }

    [ObservableProperty]
    public partial string DefaultFormat { get; set; }

    [ObservableProperty]
    public partial CompressionPreset DefaultLevel { get; set; }

    [ObservableProperty]
    public partial bool TestAfterCreate { get; set; }

    [ObservableProperty]
    public partial bool AllowUntrustedFormats { get; set; }

    [ObservableProperty]
    public partial bool LoggingEnabled { get; set; }

    [ObservableProperty]
    public partial int LogRetentionDays { get; set; }

    public void Apply()
    {
        var s = _services.Settings;
        s.Theme = Theme;
        s.ShowHiddenFiles = ShowHiddenFiles;
        s.ShowPreviews = ShowPreviews;
        s.MarkOfTheWeb = MarkOfTheWeb;
        s.ConflictPolicy = ConflictPolicy;
        s.ExtractToNewFolder = ExtractToNewFolder;
        s.OpenFolderAfterExtract = OpenFolderAfterExtract;
        s.KeepDamagedFiles = KeepDamagedFiles;
        s.RecycleReplacedFiles = RecycleReplacedFiles;
        s.WarnBeforeOpeningRiskyFiles = WarnBeforeOpeningRiskyFiles;
        s.DefaultFormat = DefaultFormat;
        s.DefaultLevel = DefaultLevel;
        s.TestAfterCreate = TestAfterCreate;
        s.AllowUntrustedFormats = AllowUntrustedFormats;
        s.LoggingEnabled = LoggingEnabled;
        s.LogRetentionDays = Math.Clamp(LogRetentionDays, 1, 365);
        _services.Log.Enabled = LoggingEnabled;
        ApplyIntegration();
        ThemeManager.SetTheme((AppTheme)Theme);
    }

    private void ApplyIntegration()
    {
        try
        {
            if (ContextMenuEnabled != ShellIntegration.IsContextMenuRegistered())
            {
                if (ContextMenuEnabled)
                {
                    ShellIntegration.RegisterContextMenu();
                }
                else
                {
                    ShellIntegration.UnregisterContextMenu();
                }
            }

            if (AssociationsEnabled != ShellIntegration.AreAssociationsRegistered())
            {
                if (AssociationsEnabled)
                {
                    ShellIntegration.RegisterAssociations();
                }
                else
                {
                    ShellIntegration.UnregisterAssociations();
                }
            }

            var hasShortcut = File.Exists(ShellIntegration.DesktopShortcut);
            if (DesktopShortcut && !hasShortcut)
            {
                ShellIntegration.CreateShortcut(ShellIntegration.DesktopShortcut);
            }
            else if (!DesktopShortcut && hasShortcut)
            {
                File.Delete(ShellIntegration.DesktopShortcut);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            _services.Log.Error("integration", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenLogs()
    {
        Directory.CreateDirectory(_services.Log.Directory);
        Shell.Open(_services.Log.Directory);
    }

    [RelayCommand]
    private void ClearLogs() => _services.Log.Clear();

    [RelayCommand]
    private void ForgetPasswords() => _services.Passwords.Clear();

    [RelayCommand]
    private void ResetAll()
    {
        var defaults = new AppSettings();
        Theme = defaults.Theme;
        ShowHiddenFiles = defaults.ShowHiddenFiles;
        ShowPreviews = defaults.ShowPreviews;
        MarkOfTheWeb = defaults.MarkOfTheWeb;
        ConflictPolicy = defaults.ConflictPolicy;
        ExtractToNewFolder = defaults.ExtractToNewFolder;
        OpenFolderAfterExtract = defaults.OpenFolderAfterExtract;
        KeepDamagedFiles = defaults.KeepDamagedFiles;
        RecycleReplacedFiles = defaults.RecycleReplacedFiles;
        WarnBeforeOpeningRiskyFiles = defaults.WarnBeforeOpeningRiskyFiles;
        DefaultFormat = defaults.DefaultFormat;
        DefaultLevel = defaults.DefaultLevel;
        TestAfterCreate = defaults.TestAfterCreate;
        AllowUntrustedFormats = defaults.AllowUntrustedFormats;
        LoggingEnabled = defaults.LoggingEnabled;
        LogRetentionDays = defaults.LogRetentionDays;
    }
}
