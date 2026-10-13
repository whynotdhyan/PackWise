using System.Windows;
using PackWise.ViewModels;

namespace PackWise.Views.Dialogs;

public partial class SettingsDialog
{
    public SettingsDialog(SettingsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;
}
