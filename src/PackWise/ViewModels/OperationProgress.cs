using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PackWise.Archiving;
using PackWise.Core.Files;
using PackWise.Views.Dialogs;

namespace PackWise.ViewModels;

/// <summary>
/// Progress state for a long-running job. Worker threads report through thread-safe methods;
/// the UI samples the latest values on a timer so heavy reporting never floods the dispatcher.
/// </summary>
public sealed partial class OperationProgress : ObservableObject
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Lock _sync = new();
    private readonly DispatcherTimer _timer;

    private ulong _done;
    private ulong _total;
    private string? _item;
    private string? _stage;
    private int _itemsDone;
    private int _itemsTotal;

    public OperationProgress(string title, string heading, IconKind icon)
    {
        Title = title;
        Heading = heading;
        Icon = icon;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    public string Title { get; }

    public IconKind Icon { get; }

    [ObservableProperty]
    public partial string Heading { get; set; }

    [ObservableProperty]
    public partial string Stage { get; set; } = "Starting…";

    [ObservableProperty]
    public partial string? CurrentItem { get; set; }

    [ObservableProperty]
    public partial double Percent { get; set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; } = true;

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TimeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanCancel { get; set; } = true;

    [ObservableProperty]
    public partial string CancelText { get; set; } = "Cancel";

    public CancellationToken Token => _cancellation.Token;

    public IProgress<ArchiveProgress> ArchiveReporter => new SynchronousProgress<ArchiveProgress>(p => ReportBytes(p.CompletedBytes, p.TotalBytes, p.CurrentItem));

    public void RequestCancel()
    {
        if (!_cancellation.IsCancellationRequested)
        {
            _cancellation.Cancel();
            CanCancel = false;
            CancelText = "Canceling…";
        }
    }

    public void SetStage(string stage)
    {
        lock (_sync)
        {
            _stage = stage;
        }
    }

    public void ReportBytes(ulong done, ulong total, string? item)
    {
        lock (_sync)
        {
            _done = done;
            _total = total;
            if (item is not null)
            {
                _item = item;
            }
        }
    }

    public void ReportItems(int done, int total, string? item)
    {
        lock (_sync)
        {
            _itemsDone = done;
            _itemsTotal = total;
            if (item is not null)
            {
                _item = item;
            }
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _cancellation.Dispose();
    }

    private void Refresh()
    {
        ulong done, total;
        int itemsDone, itemsTotal;
        string? item, stage;
        lock (_sync)
        {
            (done, total, item, stage, itemsDone, itemsTotal) = (_done, _total, _item, _stage, _itemsDone, _itemsTotal);
        }

        if (stage is not null)
        {
            Stage = stage;
        }

        CurrentItem = item;
        var elapsed = _clock.Elapsed;

        if (total > 0)
        {
            var fraction = Math.Clamp((double)done / total, 0, 1);
            IsIndeterminate = false;
            Percent = fraction * 100;
            var rate = elapsed.TotalSeconds > 0.5 ? done / elapsed.TotalSeconds : 0;
            Detail = $"{ByteSize.Format(done)} of {ByteSize.Format(total)}" + (rate > 0 ? $" · {ByteSize.FormatRate(rate)}" : string.Empty);
            TimeText = FormatTime(elapsed, fraction);
        }
        else if (itemsTotal > 0)
        {
            var fraction = Math.Clamp((double)itemsDone / itemsTotal, 0, 1);
            IsIndeterminate = false;
            Percent = fraction * 100;
            Detail = $"{itemsDone:N0} of {itemsTotal:N0}";
            TimeText = FormatTime(elapsed, fraction);
        }
        else
        {
            TimeText = $"Elapsed {Format(elapsed)}";
        }
    }

    /// <summary>Shows the remaining time only once the estimate has had time to settle.</summary>
    private static string FormatTime(TimeSpan elapsed, double fraction)
    {
        if (elapsed.TotalSeconds < 3 || fraction < 0.02 || fraction >= 1)
        {
            return $"Elapsed {Format(elapsed)}";
        }

        var remaining = TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - fraction) / fraction);
        return $"About {Format(remaining)} left";
    }

    private static string Format(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
        : $"{span.Minutes}:{span.Seconds:00}";

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}

/// <summary>Runs work on a background thread behind a modal progress dialog.</summary>
public static class Operations
{
    public static async Task<T> RunAsync<T>(Window owner, OperationProgress progress, Func<OperationProgress, T> work)
    {
        var dialog = new ProgressDialog(progress) { Owner = owner };
        Task<T>? task = null;

        dialog.Loaded += async (_, _) =>
        {
            task = Task.Run(() => work(progress), CancellationToken.None);
            try
            {
                await task.ConfigureAwait(true);
            }
            catch
            {
                // Rethrown to the caller once the dialog has closed.
            }
            finally
            {
                progress.Stop();
                dialog.Complete();
            }
        };

        dialog.ShowDialog();
        return await task!.ConfigureAwait(true);
    }
}
