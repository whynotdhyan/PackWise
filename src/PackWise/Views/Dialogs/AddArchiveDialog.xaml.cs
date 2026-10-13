using System.Windows;
using Microsoft.Win32;
using PackWise.ViewModels;

namespace PackWise.Views.Dialogs;

public partial class AddArchiveDialog
{
    private readonly AddArchiveViewModel _model;

    public AddArchiveDialog(AddArchiveViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        CreateButton.Content = model.IsAddingToExisting ? "Add" : "Create";
        Title = model.IsAddingToExisting ? "Add to existing archive" : "Add to archive";
        Loaded += (_, _) =>
        {
            PathBox.Focus();
            var name = Path.GetFileNameWithoutExtension(model.ArchivePath);
            var start = model.ArchivePath.Length - Path.GetFileName(model.ArchivePath).Length;
            PathBox.Select(start, name.Length);
            Dispatcher.BeginInvoke(() => PathBox.ScrollToHorizontalOffset(PathBox.ExtentWidth), System.Windows.Threading.DispatcherPriority.ContextIdle);
        };
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save archive as",
            FileName = Path.GetFileName(_model.ArchivePath),
            InitialDirectory = Path.GetDirectoryName(_model.ArchivePath),
            Filter = _model.Is7z ? "7-Zip archive (*.7z)|*.7z" : "ZIP archive (*.zip)|*.zip",
            OverwritePrompt = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _model.ArchivePath = dialog.FileName;
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        GeneratedBox.Visibility = Visibility.Collapsed;
        _model.SetPassword(PasswordInput.Password, ConfirmInput.Password);
    }

    private void OnGenerate(object sender, RoutedEventArgs e)
    {
        var password = AddArchiveViewModel.GeneratePassword();
        PasswordInput.Password = password;
        ConfirmInput.Password = password;
        GeneratedBox.Text = password;
        GeneratedBox.Visibility = Visibility.Visible;
        GeneratedBox.Focus();
        GeneratedBox.SelectAll();
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (_model.CanCreate)
        {
            DialogResult = true;
        }
    }
}
