using System.Windows;
using Microsoft.Win32;
using PackWise.ViewModels;

namespace PackWise.Views.Dialogs;

public partial class ExtractDialog
{
    private readonly ExtractViewModel _model;

    public ExtractDialog(ExtractViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Extract to",
            InitialDirectory = Directory.Exists(_model.Destination) ? _model.Destination : null,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _model.Destination = dialog.FolderName;
        }
    }

    private void OnExtract(object sender, RoutedEventArgs e)
    {
        if (!_model.IsDestinationValid)
        {
            MessageDialog.Error(this, "Choose a destination", "Enter a full folder path, or use Browse to pick one.");
            return;
        }

        if (_model.IsSpaceLow && !MessageDialog.Confirm(this, "Not enough free space",
                "The files may not fit on this drive. Extraction stops safely if space runs out.", "Extract anyway", MessageKind.Warning))
        {
            return;
        }

        _model.RememberChoices();
        DialogResult = true;
    }
}
