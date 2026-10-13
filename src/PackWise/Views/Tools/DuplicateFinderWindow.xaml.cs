using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PackWise.Infrastructure;
using PackWise.ViewModels;

namespace PackWise.Views.Tools;

public partial class DuplicateFinderWindow
{
    private readonly DuplicateFinderViewModel _model;

    public DuplicateFinderWindow(DuplicateFinderViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        model.AddFolderRequested += OnAddFolder;
        model.MoveToReviewRequested += OnMoveToReview;
        Drop += OnDrop;
        Closing += (_, _) => _model.CancelScanCommand.Execute(null);
    }

    private void OnAddFolder(object? sender, EventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose folders to scan", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
        {
            _model.AddFolders(dialog.FolderNames);
        }
    }

    private async void OnMoveToReview(object? sender, EventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a review folder" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (MessageDialog.Confirm(this, $"Move {_model.MarkedSummary} to the review folder?",
                $"Files keep their original folder structure under {dialog.FolderName}, so you can move them back later.", "Move files"))
        {
            await _model.MoveToReviewAsync(dialog.FolderName);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _model.AddFolders(paths);
        }
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultList.SelectedItem is DuplicateRow row)
        {
            Open(row.File.Path);
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            var rows = ResultList.SelectedItems.OfType<DuplicateRow>().ToList();
            var mark = rows.Any(r => !r.IsMarked);
            foreach (var row in rows)
            {
                row.IsMarked = mark;
            }

            e.Handled = true;
        }
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        if (ResultList.SelectedItem is DuplicateRow row)
        {
            Open(row.File.Path);
        }
    }

    private void OnRevealFile(object sender, RoutedEventArgs e)
    {
        if (ResultList.SelectedItem is DuplicateRow row)
        {
            Shell.Reveal(row.File.Path);
        }
    }

    private void OnMarkSelected(object sender, RoutedEventArgs e) => SetMarked(true);

    private void OnUnmarkSelected(object sender, RoutedEventArgs e) => SetMarked(false);

    private void SetMarked(bool value)
    {
        foreach (var row in ResultList.SelectedItems.OfType<DuplicateRow>())
        {
            row.IsMarked = value;
        }
    }

    private void Open(string path)
    {
        try
        {
            Shell.Open(path);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            MessageDialog.Error(this, "Couldn't open the file", ex.Message);
        }
    }
}
