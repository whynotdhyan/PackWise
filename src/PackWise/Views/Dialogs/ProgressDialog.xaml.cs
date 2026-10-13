using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using PackWise.ViewModels;

namespace PackWise.Views.Dialogs;

public partial class ProgressDialog
{
    private readonly OperationProgress _progress;
    private bool _completed;

    public ProgressDialog(OperationProgress progress)
    {
        InitializeComponent();
        _progress = progress;
        DataContext = progress;

        // Quick operations finish before the dialog becomes visible, avoiding a flash.
        Opacity = 0;
        var reveal = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        reveal.Tick += (_, _) =>
        {
            reveal.Stop();
            Opacity = 1;
        };
        reveal.Start();
    }

    public void Complete()
    {
        _completed = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_completed)
        {
            e.Cancel = true;
            _progress.RequestCancel();
        }

        base.OnClosing(e);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _progress.RequestCancel();
}
