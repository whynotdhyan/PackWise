using System.Reflection;
using System.Windows;
using PackWise.Archiving;
using PackWise.Infrastructure;
using PackWise.Services;

namespace PackWise.Views.Dialogs;

public partial class AboutDialog
{
    public AboutDialog()
    {
        InitializeComponent();
        StudioMark.Logo = Northstar.Design.Branding.MonogramLogo.Northstar;
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        VersionText.Text = $"Version {(plus > 0 ? version[..plus] : version)}";
        EngineText.Text = $"Archive engine: 7-Zip {ArchiveReader.EngineVersion}";
        SupportButton.Visibility = string.IsNullOrEmpty(AppLinks.Support) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRepository(object sender, RoutedEventArgs e) => Shell.OpenLink(AppLinks.Repository);

    private void OnSecurity(object sender, RoutedEventArgs e) => Shell.OpenLink(AppLinks.SecurityAdvisory);

    private void OnSupport(object sender, RoutedEventArgs e) => Shell.OpenLink(AppLinks.Support);

    private void OnLicenses(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "licenses");
        if (Directory.Exists(folder))
        {
            Shell.Open(folder);
        }
    }
}
