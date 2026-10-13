using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;
using PackWise.Browser;
using PackWise.Core.Settings;
using PackWise.Icons;
using PackWise.Services;
using PackWise.ViewModels;

namespace PackWise.Views;

public partial class MainWindow
{
    private readonly MainViewModel _model;
    private readonly AppServices _services;
    private readonly Dictionary<SortColumn, GridViewColumn> _columns = [];
    private readonly Dictionary<SortColumn, ShapePath> _sortArrows = [];
    private readonly string? _startupArgument;

    public MainWindow(AppServices services, string? startupArgument)
    {
        InitializeComponent();
        _services = services;
        _startupArgument = startupArgument;
        _model = new MainViewModel(this, services);
        DataContext = _model;

        BuildColumns();
        RestorePlacement();
        ApplyDetailsVisibility();

        _model.PropertyChanged += OnModelPropertyChanged;
        _model.SelectRequested += OnSelectRequested;
        _model.SearchFocusRequested += (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        };

        Loaded += async (_, _) =>
        {
            await _model.StartAsync(_startupArgument);
            FileList.Focus();
            if (!_services.Settings.HasSeenWelcome)
            {
                _model.ShowWelcomeCommand.Execute(null);
            }
        };

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => DropHint.Visibility = Visibility.Collapsed;
        Drop += OnDrop;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePlacement();
        _services.SaveSettings();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.Dispose();
        base.OnClosed(e);
    }

    // ---------------------------------------------------------------- Columns

    private void BuildColumns()
    {
        AddColumn(SortColumn.Name, "Name", 300, NameCellTemplate());
        AddColumn(SortColumn.Size, "Size", 88, TextCell(nameof(BrowserItem.SizeText), TextAlignment.Right));
        AddColumn(SortColumn.Packed, "Packed", 88, TextCell(nameof(BrowserItem.PackedText), TextAlignment.Right));
        AddColumn(SortColumn.Type, "Type", 150, TextCell(nameof(BrowserItem.TypeName), TextAlignment.Left, secondary: true));
        AddColumn(SortColumn.Modified, "Modified", 140, TextCell(nameof(BrowserItem.ModifiedText), TextAlignment.Left, secondary: true));
        AddColumn(SortColumn.Crc, "CRC32", 86, TextCell(nameof(BrowserItem.CrcText), TextAlignment.Left, secondary: true, mono: true));
        AddColumn(SortColumn.Location, "Folder", 240, TextCell(nameof(BrowserItem.Location), TextAlignment.Left, secondary: true));
        UpdateColumnSet();
        UpdateSortIndicators();
    }

    private void AddColumn(SortColumn key, string title, double defaultWidth, DataTemplate cell)
    {
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        var arrow = new ShapePath
        {
            Data = Geometry.Parse("M0 0L4 4 8 0"),
            StrokeThickness = 1.4,
            Margin = new Thickness(7, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        arrow.SetResourceReference(Shape.StrokeProperty, "Brush.TextSecondary");

        var header = new GridViewColumnHeader
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, arrow } },
            Command = _model.SortCommand,
            CommandParameter = key.ToString(),
            HorizontalContentAlignment = key is SortColumn.Size or SortColumn.Packed ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };

        var width = _services.Settings.ColumnWidths.TryGetValue(key.ToString(), out var saved) && saved > 20 ? saved : defaultWidth;
        var column = new GridViewColumn { Header = header, Width = width, CellTemplate = cell };
        _columns[key] = column;
        _sortArrows[key] = arrow;
    }

    private void UpdateColumnSet()
    {
        var wanted = new List<SortColumn> { SortColumn.Name };
        if (_model.ShowLocationColumn)
        {
            wanted.Add(SortColumn.Location);
        }

        wanted.Add(SortColumn.Size);
        if (_model.IsInArchive)
        {
            wanted.Add(SortColumn.Packed);
        }

        wanted.Add(SortColumn.Type);
        wanted.Add(SortColumn.Modified);
        if (_model.IsInArchive)
        {
            wanted.Add(SortColumn.Crc);
        }

        var current = Columns.Columns.ToList();
        if (current.SequenceEqual(wanted.Select(k => _columns[k])))
        {
            return;
        }

        Columns.Columns.Clear();
        foreach (var key in wanted)
        {
            Columns.Columns.Add(_columns[key]);
        }

        FitNameColumn();
    }

    private void OnListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            FitNameColumn();
        }
    }

    /// <summary>Gives the Name column whatever width the other columns leave, so nothing scrolls sideways.</summary>
    private void FitNameColumn()
    {
        if (FileList.ActualWidth <= 0)
        {
            return;
        }

        var others = Columns.Columns.Where(c => c != _columns[SortColumn.Name]).Sum(c => double.IsNaN(c.Width) ? c.ActualWidth : c.Width);
        var available = FileList.ActualWidth - others - SystemParameters.VerticalScrollBarWidth - 12;
        _columns[SortColumn.Name].Width = Math.Max(200, available);
    }

    private void UpdateSortIndicators()
    {
        foreach (var (key, arrow) in _sortArrows)
        {
            arrow.Visibility = key == _model.SortColumn ? Visibility.Visible : Visibility.Collapsed;
            arrow.RenderTransformOrigin = new Point(0.5, 0.5);
            arrow.RenderTransform = _model.SortDescending ? null : new RotateTransform(180);
        }
    }

    private static DataTemplate NameCellTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(DockPanel));
        panel.SetValue(MarginProperty, new Thickness(4, 0, 0, 0));

        var icon = new FrameworkElementFactory(typeof(FileTypeIcon));
        icon.SetValue(WidthProperty, 18.0);
        icon.SetValue(HeightProperty, 18.0);
        icon.SetValue(MarginProperty, new Thickness(0, 0, 9, 0));
        icon.SetBinding(FileTypeIcon.KindProperty, new Binding(nameof(BrowserItem.Kind)));
        icon.SetBinding(FileTypeIcon.IsOverlayLockProperty, new Binding(nameof(BrowserItem.IsEncrypted)));
        icon.SetBinding(FileTypeIcon.IsOverlayWarningProperty, new Binding(nameof(BrowserItem.IsRisky)));
        panel.AppendChild(icon);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(BrowserItem.Name)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetBinding(ToolTipProperty, new Binding(nameof(BrowserItem.RiskText)));
        text.SetBinding(OpacityProperty, new Binding(nameof(BrowserItem.IsHidden)) { Converter = new HiddenOpacityConverter() });
        panel.AppendChild(text);

        return new DataTemplate { VisualTree = panel };
    }

    private static DataTemplate TextCell(string property, TextAlignment alignment, bool secondary = false, bool mono = false)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(property));
        text.SetValue(TextBlock.TextAlignmentProperty, alignment);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(MarginProperty, new Thickness(2, 0, 6, 0));
        if (secondary)
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        }

        if (mono)
        {
            text.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
            text.SetValue(TextBlock.FontSizeProperty, 12.0);
        }

        return new DataTemplate { VisualTree = text };
    }

    // ---------------------------------------------------------------- List interaction

    private async void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && ItemFromSource(e.OriginalSource) is { } item)
        {
            await _model.OpenItemAsync(item);
        }
    }

    private async void OnListKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when FileList.SelectedItem is BrowserItem item && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                await _model.OpenItemAsync(item);
                break;
            case Key.Back:
                e.Handled = true;
                await _model.GoUpCommand.ExecuteAsync(null);
                break;
            case Key.Delete:
                e.Handled = true;
                if (_model.DeleteCommand.CanExecute(null))
                {
                    await _model.DeleteCommand.ExecuteAsync(null);
                }

                break;
            case Key.Escape when _model.FilterText.Length > 0:
                e.Handled = true;
                _model.FilterText = string.Empty;
                break;
        }
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e) => _model.UpdateSelection(FileList.SelectedItems);

    private void OnSelectRequested(object? sender, BrowserItem item)
    {
        Dispatcher.BeginInvoke(() =>
        {
            FileList.SelectedItem = item;
            FileList.ScrollIntoView(item);
            if (FileList.ItemContainerGenerator.ContainerFromItem(item) is ListViewItem container)
            {
                container.Focus();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static BrowserItem? ItemFromSource(object source)
    {
        var element = source as DependencyObject;
        while (element is not null and not ListViewItem)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return (element as ListViewItem)?.DataContext as BrowserItem;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _model.FilterText = string.Empty;
            FileList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            FileList.Focus();
            if (FileList.Items.Count > 0)
            {
                FileList.SelectedIndex = 0;
            }

            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- Address bar

    private void OnCrumbBarClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<Button>(source) is not null)
        {
            return;
        }

        AddressBox.Visibility = Visibility.Visible;
        CrumbBar.Visibility = Visibility.Hidden;
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private void OnCrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Breadcrumb crumb })
        {
            _model.NavigateBreadcrumb(crumb);
        }
    }

    private async void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var text = AddressBox.Text;
            CloseAddressEditor();
            await _model.NavigateToPathAsync(text);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _model.AddressText = _model.CurrentLocation.DisplayPath;
            CloseAddressEditor();
        }
    }

    private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CloseAddressEditor();

    private void CloseAddressEditor()
    {
        AddressBox.Visibility = Visibility.Collapsed;
        CrumbBar.Visibility = Visibility.Visible;
        FileList.Focus();
    }

    // ---------------------------------------------------------------- Drag and drop

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        DropHintText.Text = _model.IsInArchive
            ? "Drop to add to this archive"
            : paths.Length == 1 && Archiving.ArchiveReader.LooksLikeArchive(paths[0]) ? "Drop to open the archive" : "Drop to create an archive";
        DropHint.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            Activate();
            await _model.HandleDropAsync(paths);
        }
    }

    // ---------------------------------------------------------------- Model changes

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsInArchive):
            case nameof(MainViewModel.ShowLocationColumn):
                UpdateColumnSet();
                break;
            case nameof(MainViewModel.SortColumn):
            case nameof(MainViewModel.SortDescending):
                UpdateSortIndicators();
                break;
            case nameof(MainViewModel.ShowDetails):
                ApplyDetailsVisibility();
                break;
            case nameof(MainViewModel.Items):
                EmptyHint.Visibility = _model.Items.Cast<BrowserItem>().All(i => i.Type == BrowserItemType.ParentLink) && !_model.IsInArchive
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                break;
            case nameof(MainViewModel.CurrentLocation):
                LocationIcon.Kind = _model.CurrentLocation switch
                {
                    ArchiveLocation => IconKind.Archive,
                    ComputerLocation => IconKind.Computer,
                    _ => IconKind.Folder,
                };
                Dispatcher.BeginInvoke(CrumbScroller.ScrollToRightEnd, System.Windows.Threading.DispatcherPriority.Loaded);
                break;
        }
    }

    private void ApplyDetailsVisibility()
    {
        var show = _model.ShowDetails;
        DetailsPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        DetailsSplitter.Visibility = DetailsPane.Visibility;
        SplitterColumn.Width = new GridLength(show ? 10 : 0);
        DetailsColumn.MinWidth = show ? 220 : 0;
        DetailsColumn.Width = show ? new GridLength(Math.Clamp(_services.Settings.DetailsPaneWidth, 220, 560)) : new GridLength(0);
    }

    // ---------------------------------------------------------------- Placement

    private void RestorePlacement()
    {
        if (_services.Settings.MainWindow is not { } placement)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var bounds = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(bounds) || placement.Width < MinWidth || placement.Height < MinHeight)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _services.Settings.MainWindow = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized);
        foreach (var (key, column) in _columns)
        {
            _services.Settings.ColumnWidths[key.ToString()] = column.ActualWidth > 0 ? column.ActualWidth : column.Width;
        }

        if (_model.ShowDetails && DetailsColumn.ActualWidth > 0)
        {
            _services.Settings.DetailsPaneWidth = DetailsColumn.ActualWidth;
        }
    }

    private static T? FindParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        var current = child;
        while (current is not null and not T)
        {
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }

        return current as T;
    }

    private sealed class HiddenOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is true ? 0.55 : 1.0;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
