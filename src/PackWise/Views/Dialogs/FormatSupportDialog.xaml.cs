using PackWise.Archiving;

namespace PackWise.Views.Dialogs;

public partial class FormatSupportDialog
{
    public FormatSupportDialog(bool allowUntrusted)
    {
        InitializeComponent();
        Intro.Text = $"Engine: 7-Zip {ArchiveReader.EngineVersion}. Formats marked “Off” are rarely needed and stay closed unless you enable them in Settings › Formats.";
        FormatList.ItemsSource = ArchiveReader.Formats
            .OrderBy(f => !f.IsTrusted)
            .ThenBy(f => f.Category)
            .ThenBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(f => new
            {
                Name = f.DisplayName,
                Extensions = string.Join(", ", f.Extensions.Select(e => "." + e)),
                Open = f.IsTrusted ? "Yes" : allowUntrusted ? "Yes (enabled)" : "Off",
                Extract = f.IsTrusted || allowUntrusted ? "Yes" : "—",
                Create = f.CanCreate ? "Yes" : "—",
                Encryption = f.CanCreateEncrypted ? "AES-256" : f.CanReadEncrypted ? "Read only" : "—",
            })
            .ToList();
    }
}
