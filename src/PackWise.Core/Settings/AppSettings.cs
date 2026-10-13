using PackWise.Archiving;
using PackWise.Core.Extraction;
using PackWise.Core.Security;

namespace PackWise.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>User preferences. Stored locally as JSON; never contains passwords or file contents.</summary>
public sealed class AppSettings
{
    public const int MaxRecentItems = 12;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public MarkOfTheWebPolicy MarkOfTheWeb { get; set; } = MarkOfTheWebPolicy.AllFiles;

    public bool AllowUntrustedFormats { get; set; }

    public ConflictPolicy ConflictPolicy { get; set; } = ConflictPolicy.Ask;

    public bool ExtractToNewFolder { get; set; } = true;

    public bool OpenFolderAfterExtract { get; set; } = true;

    public bool KeepDamagedFiles { get; set; }

    public bool RecycleReplacedFiles { get; set; } = true;

    public bool WarnBeforeOpeningRiskyFiles { get; set; } = true;

    public string DefaultFormat { get; set; } = "zip";

    public CompressionPreset DefaultLevel { get; set; } = CompressionPreset.Balanced;

    public bool TestAfterCreate { get; set; } = true;

    public bool ShowHiddenFiles { get; set; }

    public bool ShowDetailsPane { get; set; } = true;

    /// <summary>Preview images and text in the details pane (decoded with Windows' built-in codecs, never executed).</summary>
    public bool ShowPreviews { get; set; } = true;

    public bool ShowToolbarLabels { get; set; } = true;

    public bool LoggingEnabled { get; set; } = true;

    /// <summary>The getting-started tour has been shown (it appears once, then from Help).</summary>
    public bool HasSeenWelcome { get; set; }

    public int LogRetentionDays { get; set; } = 14;

    public string? LastFolder { get; set; }

    public List<string> Favorites { get; set; } = [];

    public List<string> RecentArchives { get; set; } = [];

    public List<string> RecentDestinations { get; set; } = [];

    public WindowPlacement? MainWindow { get; set; }

    public Dictionary<string, double> ColumnWidths { get; set; } = [];

    public double DetailsPaneWidth { get; set; } = 300;

    public void Remember(List<string> list, string value)
    {
        list.RemoveAll(v => v.Equals(value, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, value);
        if (list.Count > MaxRecentItems)
        {
            list.RemoveRange(MaxRecentItems, list.Count - MaxRecentItems);
        }
    }
}

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
