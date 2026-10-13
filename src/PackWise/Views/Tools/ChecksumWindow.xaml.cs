using System.Windows;
using Microsoft.Win32;
using PackWise.Infrastructure;
using PackWise.ViewModels;

namespace PackWise.Views.Tools;

public partial class ChecksumWindow
{
    private readonly ChecksumViewModel _model;
    private readonly List<System.Windows.Controls.GridViewColumn> _allColumns;

    public ChecksumWindow(ChecksumViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        model.SaveRequested += OnSave;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ChecksumViewModel.UseCrc32) or nameof(ChecksumViewModel.UseSha256) or nameof(ChecksumViewModel.UseSha512))
            {
                UpdateColumns();
            }
        };
        _allColumns = [.. ComputeColumns.Columns];
        UpdateColumns();
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                _model.AddFiles(paths);
            }
        };
        Loaded += (_, _) =>
        {
            if (_model.ComputeCommand.CanExecute(null))
            {
                _model.ComputeCommand.Execute(null);
            }
        };
        Closing += (_, _) => _model.CancelCommand.Execute(null);
    }

    /// <summary>Shows only the columns for the selected algorithms.</summary>
    private void UpdateColumns()
    {
        ComputeColumns.Columns.Clear();
        foreach (var column in _allColumns)
        {
            var visible = column == CrcColumn ? _model.UseCrc32 : column == Sha256Column ? _model.UseSha256 : column != Sha512Column || _model.UseSha512;
            if (visible)
            {
                ComputeColumns.Columns.Add(column);
            }
        }
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose files", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
        {
            _model.AddFiles(dialog.FileNames);
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var first = _model.Rows.FirstOrDefault(r => r.Result is not null);
        var dialog = new SaveFileDialog
        {
            Title = "Save checksum file",
            FileName = _model.Rows.Count == 1 && first is not null ? first.Name + ".sha256" : "checksums.sha256",
            InitialDirectory = first is null ? null : Path.GetDirectoryName(first.Path),
            Filter = "SHA-256 checksum file (*.sha256)|*.sha256|SHA-512 checksum file (*.sha512)|*.sha512|Text file (*.txt)|*.txt",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _model.BuildChecksumFile(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageDialog.Error(this, "Couldn't save the checksum file", ex.Message);
        }
    }

    private async void OnOpenChecksumFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open checksum file",
            Filter = "Checksum files|*.sha256;*.sha512;*.sfv;*.txt;*.sum|All files|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _model.VerifyFileCommand.ExecuteAsync(dialog.FileName);
        }
    }

    private void OnCopySha256(object sender, RoutedEventArgs e) => Copy(r => r.Sha256);

    private void OnCopyCrc(object sender, RoutedEventArgs e) => Copy(r => r.Crc32);

    private void OnCopySha512(object sender, RoutedEventArgs e) => Copy(r => r.Sha512);

    private void Copy(Func<ChecksumRow, string?> selector)
    {
        if (ResultList.SelectedItem is ChecksumRow row && selector(row) is { } value)
        {
            Shell.CopyText(value);
        }
    }
}
