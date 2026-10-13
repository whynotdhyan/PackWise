using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PackWise.Archiving;
using PackWise.Browser;
using PackWise.Core.Archives;
using PackWise.Core.Files;
using PackWise.Core.Security;
using PackWise.Infrastructure;
using PackWise.Services;
using PackWise.Views.Dialogs;

namespace PackWise.ViewModels;

/// <summary>
/// State and navigation for the main window: one list that browses drives, folders and the
/// inside of archives the same way.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int PreviewTextLimit = 64 * 1024;
    private const long PreviewImageLimit = 24L * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".webp", ".jfif" };

    private readonly Window _owner;
    private readonly AppServices _services;
    private readonly UiPasswordProvider _passwordProvider;
    private readonly Stack<Location> _back = new();
    private readonly Stack<Location> _forward = new();
    private readonly DispatcherTimer _refreshDebounce;
    private List<BrowserItem> _items = [];
    private ListCollectionView _view;
    private ArchiveSession? _session;
    private CancellationTokenSource? _previewCancellation;
    private FileSystemWatcher? _watcher;
    private IReadOnlyList<BrowserItem> _selection = [];
    private string? _pendingSelection;
    private int _navigationVersion;

    public MainViewModel(Window owner, AppServices services)
    {
        _owner = owner;
        _services = services;
        _passwordProvider = new UiPasswordProvider(WindowHelper.ActiveOrMain);
        _view = CreateView([]);
        ShowDetails = services.Settings.ShowDetailsPane;
        Favorites = new ObservableCollection<string>(services.Settings.Favorites);
        RecentArchives = new ObservableCollection<string>(services.Settings.RecentArchives);
        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _refreshDebounce.Tick += async (_, _) =>
        {
            _refreshDebounce.Stop();
            await RefreshAsync();
        };
    }

    /// <summary>Raised after a listing so the view can select and reveal an item by name.</summary>
    public event EventHandler<BrowserItem>? SelectRequested;

    public event EventHandler? SearchFocusRequested;

    public ICollectionView Items => _view;

    public ObservableCollection<Breadcrumb> Breadcrumbs { get; } = [];

    public ObservableCollection<InfoRow> DetailsRows { get; } = [];

    public ObservableCollection<string> Favorites { get; }

    public ObservableCollection<string> RecentArchives { get; }

    public ArchiveSession? Session => _session;

    [ObservableProperty]
    public partial Location CurrentLocation { get; private set; } = ComputerLocation.Instance;

    [ObservableProperty]
    public partial string AddressText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand), nameof(InfoCommand))]
    public partial bool IsInArchive { get; private set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? BusyText { get; private set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SearchWholeArchive { get; set; }

    [ObservableProperty]
    public partial bool ShowLocationColumn { get; private set; }

    [ObservableProperty]
    public partial string TotalText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectionText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? FormatBadge { get; private set; }

    [ObservableProperty]
    public partial bool ArchiveEncrypted { get; private set; }

    [ObservableProperty]
    public partial bool ArchiveFromInternet { get; private set; }

    [ObservableProperty]
    public partial bool ArchiveHasIssues { get; private set; }

    [ObservableProperty]
    public partial bool ShowDetails { get; set; }

    [ObservableProperty]
    public partial Notice? Banner { get; private set; }

    [ObservableProperty]
    public partial string DetailsTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailsSubtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial FileKind DetailsKind { get; private set; } = FileKind.Folder;

    [ObservableProperty]
    public partial string? DetailsWarning { get; private set; }

    [ObservableProperty]
    public partial ImageSource? PreviewImage { get; private set; }

    [ObservableProperty]
    public partial string? PreviewText { get; private set; }

    [ObservableProperty]
    public partial string? PreviewNote { get; private set; }

    [ObservableProperty]
    public partial SortColumn SortColumn { get; private set; } = SortColumn.Name;

    [ObservableProperty]
    public partial bool SortDescending { get; private set; }

    public IReadOnlyList<BrowserItem> SelectedItems => _selection;

    public string WindowTitle => CurrentLocation switch
    {
        ArchiveLocation archive => $"{Path.GetFileName(archive.ArchivePath)} - PackWise",
        FolderLocation folder => $"{(Path.GetFileName(folder.Path.TrimEnd('\\')) is { Length: > 0 } name ? name : folder.Path)} - PackWise",
        _ => "PackWise",
    };

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    public bool CanGoUp => CurrentLocation is not ComputerLocation;

    // ---------------------------------------------------------------- Startup

    public async Task StartAsync(string? argument)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            await NavigateToPathAsync(argument);
            return;
        }

        var start = _services.Settings.LastFolder is { } last && Directory.Exists(last)
            ? last
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await NavigateAsync(new FolderLocation(start));
    }

    // ---------------------------------------------------------------- Navigation

    public async Task NavigateToPathAsync(string text)
    {
        var path = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"'));
        if (path.Length == 0 || path.Equals("This PC", StringComparison.OrdinalIgnoreCase))
        {
            await NavigateAsync(ComputerLocation.Instance);
            return;
        }

        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            MessageDialog.Error(_owner, "That isn't a valid path", text);
            return;
        }

        if (Directory.Exists(path))
        {
            await NavigateAsync(new FolderLocation(path));
        }
        else if (File.Exists(path))
        {
            if (ArchiveReader.LooksLikeArchive(path) || ArchiveReader.Detect(path, _services.Settings.AllowUntrustedFormats) is not null)
            {
                await OpenArchiveAsync(path);
            }
            else
            {
                _pendingSelection = Path.GetFileName(path);
                await NavigateAsync(new FolderLocation(Path.GetDirectoryName(path)!));
            }
        }
        else if (FindArchiveInPath(path) is { } split)
        {
            await OpenArchiveAsync(split.Archive, innerPath: split.Inner);
        }
        else
        {
            MessageDialog.Error(_owner, "Can't find that location", $"“{text}” doesn't exist or can't be reached.");
            AddressText = CurrentLocation.DisplayPath;
        }
    }

    public Task OpenArchiveAsync(string path, Location? parent = null, string innerPath = "")
    {
        var display = parent is ArchiveLocation outer ? $"{outer.DisplayPath}\\{Path.GetFileName(path)}" : path;
        return NavigateAsync(new ArchiveLocation(path, innerPath, display, parent ?? new FolderLocation(Path.GetDirectoryName(path)!)));
    }

    public async Task NavigateAsync(Location target, bool recordHistory = true)
    {
        var version = ++_navigationVersion;
        CancelPreview();
        FilterText = string.Empty;

        try
        {
            if (target is ArchiveLocation requested && !await EnsureSessionAsync(requested))
            {
                return;
            }

            var items = target switch
            {
                ComputerLocation => await Task.Run(ListDrives),
                FolderLocation folder => await ListFolderAsync(folder.Path),
                ArchiveLocation archive => ListArchive(archive),
                _ => [],
            };

            if (version != _navigationVersion)
            {
                return;
            }

            if (target is not ArchiveLocation)
            {
                CloseSession();
            }

            if (recordHistory && CurrentLocation != target && !IsSameArchiveFolder(CurrentLocation, target))
            {
                _back.Push(CurrentLocation);
                _forward.Clear();
            }

            CurrentLocation = target;
            ShowItems(items);
            AfterNavigation();
        }
        catch (UnauthorizedAccessException)
        {
            MessageDialog.Error(_owner, "Access denied", $"PackWise doesn't have permission to open “{target.DisplayPath}”.");
        }
        catch (IOException ex)
        {
            MessageDialog.Error(_owner, "Couldn't open this location", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task GoBack()
    {
        if (_back.TryPop(out var target))
        {
            _forward.Push(CurrentLocation);
            await NavigateAsync(target, recordHistory: false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private async Task GoForward()
    {
        if (_forward.TryPop(out var target))
        {
            _back.Push(CurrentLocation);
            await NavigateAsync(target, recordHistory: false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private async Task GoUp()
    {
        switch (CurrentLocation)
        {
            case FolderLocation folder:
                var trimmed = Path.TrimEndingDirectorySeparator(folder.Path);
                var parent = Path.GetDirectoryName(trimmed);
                _pendingSelection = parent is null ? folder.Path : Path.GetFileName(trimmed);
                await NavigateAsync(parent is null ? ComputerLocation.Instance : new FolderLocation(parent));
                break;

            case ArchiveLocation { InnerPath.Length: > 0 } archive:
                var slash = archive.InnerPath.LastIndexOf('\\');
                _pendingSelection = archive.InnerPath[(slash + 1)..];
                await NavigateAsync(archive.WithInnerPath(slash < 0 ? string.Empty : archive.InnerPath[..slash]));
                break;

            case ArchiveLocation archive:
                _pendingSelection = archive.Parent is ArchiveLocation ? Path.GetFileName(archive.DisplayRoot) : Path.GetFileName(archive.ArchivePath);
                await NavigateAsync(archive.Parent ?? new FolderLocation(Path.GetDirectoryName(archive.ArchivePath)!));
                break;
        }
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    public async Task RefreshAsync()
    {
        _pendingSelection ??= _selection.FirstOrDefault()?.Name;
        if (CurrentLocation is ArchiveLocation && _session is not null)
        {
            ShowItems(ListArchive((ArchiveLocation)CurrentLocation));
            AfterNavigation();
        }
        else
        {
            await NavigateAsync(CurrentLocation, recordHistory: false);
        }
    }

    public async Task OpenItemAsync(BrowserItem item)
    {
        switch (item.Type)
        {
            case BrowserItemType.ParentLink:
                await GoUp();
                break;
            case BrowserItemType.Drive:
            case BrowserItemType.Folder:
                await NavigateAsync(new FolderLocation(item.FullPath!));
                break;
            case BrowserItemType.ArchiveFolder when CurrentLocation is ArchiveLocation archive:
                await NavigateAsync(archive.WithInnerPath(item.Node!.Path));
                break;
            case BrowserItemType.File:
                if (item.Kind == FileKind.Archive || ArchiveReader.LooksLikeArchive(item.FullPath!))
                {
                    await OpenArchiveAsync(item.FullPath!);
                }
                else
                {
                    OpenWithShell(item.FullPath!);
                }

                break;
            case BrowserItemType.ArchiveFile:
                if (ArchiveReader.LooksLikeArchive(item.Name))
                {
                    await OpenNestedArchiveAsync(item);
                }
                else
                {
                    await ViewArchiveItemAsync(item);
                }

                break;
        }
    }

    public void NavigateBreadcrumb(Breadcrumb crumb) => _ = NavigateAsync(crumb.Target);

    // ---------------------------------------------------------------- Selection and details

    public void UpdateSelection(IList selected)
    {
        _selection = selected.OfType<BrowserItem>().Where(i => i.Type != BrowserItemType.ParentLink).ToList();
        UpdateSelectionText();
        UpdateDetails();
        ExtractCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ViewCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Sort(string column)
    {
        var parsed = Enum.Parse<SortColumn>(column);
        SortDescending = parsed == SortColumn && !SortDescending;
        SortColumn = parsed;
        _view.CustomSort = new BrowserItemComparer(SortColumn, SortDescending);
    }

    [RelayCommand]
    private void ToggleDetails()
    {
        ShowDetails = !ShowDetails;
        _services.Settings.ShowDetailsPane = ShowDetails;
        UpdateDetails();
    }

    [RelayCommand]
    private void FocusSearch() => SearchFocusRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void DismissBanner() => Banner = null;

    [RelayCommand]
    private void RunBannerAction()
    {
        var action = Banner?.Action;
        Banner = null;
        action?.Invoke();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSearchWholeArchiveChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        var text = FilterText.Trim();
        if (IsInArchive && SearchWholeArchive && text.Length > 0 && _session is not null)
        {
            var matches = _session.Root.DescendantEntries()
                .Where(e => e.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                .Select(e => _session.Root.Find(e.Path))
                .OfType<ArchiveNode>()
                .Select(n => ToArchiveItem(n, withLocation: true))
                .ToList();
            ShowLocationColumn = true;
            SetView(matches, filter: null);
            return;
        }

        if (ShowLocationColumn && CurrentLocation is ArchiveLocation location)
        {
            ShowLocationColumn = false;
            SetView(ListArchive(location), filter: null);
        }

        _view.Filter = text.Length == 0 ? null : o => o is BrowserItem item && (item.Type == BrowserItemType.ParentLink || item.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase));
        UpdateTotals();
    }

    // ---------------------------------------------------------------- Listing

    private async Task<bool> EnsureSessionAsync(ArchiveLocation location)
    {
        if (_session is not null && _session.FilePath.Equals(location.ArchivePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        IsBusy = true;
        BusyText = $"Opening {Path.GetFileName(location.ArchivePath)}…";
        try
        {
            var session = await ArchiveSession.OpenAsync(location, _passwordProvider);
            CloseSession();
            _session = session;
            OnPropertyChanged(nameof(Session));

            if (!location.IsNested)
            {
                _services.Settings.Remember(_services.Settings.RecentArchives, location.ArchivePath);
                SyncRecent();
            }

            _services.Log.Info("open", $"Opened {session.Info.Format.DisplayName} archive with {session.Reader.Entries.Count} entries.", location.ArchivePath);
            if (session.Info.Errors != ArchiveIssues.None || session.Info.Warnings != ArchiveIssues.None)
            {
                Banner = new Notice(DescribeIssues(session.Info), NoticeKind.Warning, "Test archive", () => TestCommand.Execute(null));
            }

            return true;
        }
        catch (ArchivePasswordException ex)
        {
            MessageDialog.Error(_owner, "Password needed", ex.Message);
        }
        catch (ArchiveNotRecognizedException ex)
        {
            MessageDialog.Error(_owner, "Can't open this file", ex.Message);
        }
        catch (ArchiveException ex)
        {
            MessageDialog.Error(_owner, "Can't open this archive", ex.Message);
        }
        catch (IOException ex)
        {
            MessageDialog.Error(_owner, "Can't open this archive", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageDialog.Error(_owner, "Access denied", ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }

        AddressText = CurrentLocation.DisplayPath;
        return false;
    }

    private List<BrowserItem> ListDrives()
    {
        var items = new List<BrowserItem>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            string? label = null;
            string detail;
            try
            {
                if (drive.IsReady)
                {
                    label = drive.VolumeLabel;
                    detail = $"{ByteSize.Format(drive.AvailableFreeSpace)} free of {ByteSize.Format(drive.TotalSize)}";
                }
                else
                {
                    detail = "Not ready";
                }
            }
            catch (IOException)
            {
                detail = "Not available";
            }
            catch (UnauthorizedAccessException)
            {
                detail = "Access denied";
            }

            var kind = drive.DriveType switch
            {
                DriveType.Removable => "Removable drive",
                DriveType.Network => "Network drive",
                DriveType.CDRom => "CD/DVD drive",
                _ => string.IsNullOrEmpty(label) ? "Local Disk" : label,
            };

            items.Add(new BrowserItem
            {
                Type = BrowserItemType.Drive,
                Name = $"{kind} ({drive.Name.TrimEnd('\\')})",
                FullPath = drive.Name,
                DriveDetail = detail,
                Kind = FileKind.DiskImage,
            });
        }

        return items;
    }

    private Task<List<BrowserItem>> ListFolderAsync(string path)
    {
        var showHidden = _services.Settings.ShowHiddenFiles;
        return Task.Run(() =>
        {
            var directory = new DirectoryInfo(path);
            if (!directory.Exists)
            {
                throw new IOException($"“{path}” no longer exists.");
            }

            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
            };

            var items = new List<BrowserItem> { BrowserItem.Parent() };
            foreach (var info in directory.EnumerateFileSystemInfos("*", options))
            {
                var isDirectory = info is DirectoryInfo;
                items.Add(new BrowserItem
                {
                    Type = isDirectory ? BrowserItemType.Folder : BrowserItemType.File,
                    Name = info.Name,
                    FullPath = info.FullName,
                    Size = info is FileInfo file ? (ulong)file.Length : null,
                    Modified = info.LastWriteTimeUtc,
                    IsHidden = info.Attributes.HasFlag(FileAttributes.Hidden),
                    Kind = FileKinds.FromName(info.Name, isDirectory),
                    Risk = isDirectory ? FileRisk.None : FileRiskClassifier.Classify(info.Name),
                });
            }

            return items;
        });
    }

    private List<BrowserItem> ListArchive(ArchiveLocation location)
    {
        var node = _session!.Root.Find(location.InnerPath) ?? _session.Root;
        var items = new List<BrowserItem> { BrowserItem.Parent() };
        items.AddRange(node.Children.Select(child => ToArchiveItem(child, withLocation: false)));
        return items;
    }

    private static BrowserItem ToArchiveItem(ArchiveNode node, bool withLocation) => new()
    {
        Type = node.IsDirectory ? BrowserItemType.ArchiveFolder : BrowserItemType.ArchiveFile,
        Name = node.Name,
        Node = node,
        Size = node.Size,
        PackedSize = node.PackedSize,
        Modified = node.Modified,
        Crc = node.Entry?.Crc,
        IsEncrypted = node.IsEncrypted,
        Kind = FileKinds.FromName(node.Name, node.IsDirectory),
        Risk = node.IsDirectory ? FileRisk.None : FileRiskClassifier.Classify(node.Name),
        Location = withLocation ? node.Parent?.Path ?? string.Empty : null,
    };

    private void ShowItems(List<BrowserItem> items)
    {
        ShowLocationColumn = false;
        SetView(items, filter: null);
    }

    private void SetView(List<BrowserItem> items, Predicate<object>? filter)
    {
        _items = items;
        _view = CreateView(items);
        _view.Filter = filter;
        OnPropertyChanged(nameof(Items));
        UpdateTotals();

        if (_pendingSelection is { } name)
        {
            _pendingSelection = null;
            if (items.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                          (i.FullPath is not null && i.FullPath.Equals(name, StringComparison.OrdinalIgnoreCase))) is { } match)
            {
                SelectRequested?.Invoke(this, match);
            }
        }
    }

    private ListCollectionView CreateView(List<BrowserItem> items) => new(items)
    {
        CustomSort = new BrowserItemComparer(SortColumn, SortDescending),
    };

    private void AfterNavigation()
    {
        IsInArchive = CurrentLocation is ArchiveLocation;
        AddressText = CurrentLocation.DisplayPath;
        BuildBreadcrumbs();
        UpdateArchiveIndicators();
        WatchFolder(CurrentLocation as FolderLocation);
        _selection = [];
        UpdateSelectionText();
        UpdateDetails();

        if (CurrentLocation is FolderLocation folder)
        {
            _services.Settings.LastFolder = folder.Path;
        }

        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
        ExtractCommand.NotifyCanExecuteChanged();
        TestCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ViewCommand.NotifyCanExecuteChanged();
        InfoCommand.NotifyCanExecuteChanged();
    }

    private void BuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        var crumbs = new List<Breadcrumb>();
        AddCrumbs(CurrentLocation, crumbs);
        foreach (var crumb in crumbs)
        {
            Breadcrumbs.Add(crumb);
        }
    }

    private static void AddCrumbs(Location location, List<Breadcrumb> crumbs)
    {
        switch (location)
        {
            case ComputerLocation:
                crumbs.Add(new Breadcrumb("This PC", ComputerLocation.Instance, false));
                break;

            case FolderLocation folder:
                crumbs.Add(new Breadcrumb("This PC", ComputerLocation.Instance, false));
                var root = Path.GetPathRoot(folder.Path) ?? folder.Path;
                crumbs.Add(new Breadcrumb(root.TrimEnd('\\'), new FolderLocation(root), false));
                var current = root;
                foreach (var part in folder.Path[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, part);
                    crumbs.Add(new Breadcrumb(part, new FolderLocation(current), false));
                }

                break;

            case ArchiveLocation archive:
                if (archive.Parent is ArchiveLocation outer)
                {
                    AddCrumbs(outer, crumbs);
                }
                else
                {
                    AddCrumbs(new FolderLocation(Path.GetDirectoryName(archive.ArchivePath)!), crumbs);
                }

                var rootName = archive.Parent is ArchiveLocation ? Path.GetFileName(archive.DisplayRoot) : Path.GetFileName(archive.ArchivePath);
                crumbs.Add(new Breadcrumb(rootName, archive.WithInnerPath(string.Empty), true));
                var inner = string.Empty;
                foreach (var part in archive.InnerPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
                {
                    inner = inner.Length == 0 ? part : inner + "\\" + part;
                    crumbs.Add(new Breadcrumb(part, archive.WithInnerPath(inner), true));
                }

                break;
        }
    }

    private void UpdateTotals()
    {
        var visible = _view.Cast<BrowserItem>().Where(i => i.Type != BrowserItemType.ParentLink).ToList();
        var folders = visible.Count(i => i.IsFolderLike);
        var files = visible.Count - folders;
        var size = visible.Where(i => !i.IsFolderLike || i.Type == BrowserItemType.ArchiveFolder).Aggregate(0UL, (sum, i) => sum + (i.Size ?? 0));

        var parts = new List<string>();
        if (folders > 0)
        {
            parts.Add(Plural(folders, "folder"));
        }

        parts.Add(Plural(files, "file"));
        var text = string.Join(", ", parts);
        if (CurrentLocation is not ComputerLocation)
        {
            text += $" · {ByteSize.Format(size)}";
        }

        if (IsInArchive && _session is not null && CurrentLocation is ArchiveLocation { InnerPath.Length: 0 })
        {
            text += $" · {ByteSize.Format(_session.Info.PhysicalSize ?? 0)} on disk";
        }

        TotalText = text;
    }

    private void UpdateSelectionText()
    {
        if (_selection.Count == 0)
        {
            SelectionText = string.Empty;
            return;
        }

        var size = _selection.Aggregate(0UL, (sum, i) => sum + (i.Size ?? 0));
        SelectionText = $"{Plural(_selection.Count, "item")} selected" + (size > 0 ? $" · {ByteSize.Format(size)}" : string.Empty);
    }

    private void UpdateArchiveIndicators()
    {
        if (_session is { } session && IsInArchive)
        {
            FormatBadge = string.Join(" › ", session.Info.FormatChain.Select(f => f.DisplayName));
            ArchiveEncrypted = session.Info.HasEncryptedHeaders || session.Reader.Entries.Any(e => e.IsEncrypted);
            ArchiveFromInternet = session.CameFromInternet;
            ArchiveHasIssues = session.Info.Errors != ArchiveIssues.None;
        }
        else
        {
            FormatBadge = null;
            ArchiveEncrypted = false;
            ArchiveFromInternet = false;
            ArchiveHasIssues = false;
        }
    }

    // ---------------------------------------------------------------- Details pane

    private void UpdateDetails()
    {
        CancelPreview();
        PreviewImage = null;
        PreviewText = null;
        PreviewNote = null;
        DetailsWarning = null;
        DetailsRows.Clear();

        if (!ShowDetails)
        {
            return;
        }

        if (_selection.Count == 1)
        {
            DescribeItem(_selection[0]);
            StartPreview(_selection[0]);
        }
        else if (_selection.Count > 1)
        {
            DetailsTitle = $"{_selection.Count} items";
            DetailsSubtitle = "Selected";
            DetailsKind = FileKind.Generic;
            var size = _selection.Aggregate(0UL, (sum, i) => sum + (i.Size ?? 0));
            DetailsRows.Add(new InfoRow("Total size", ByteSize.Format(size)));
            DetailsRows.Add(new InfoRow("Folders", _selection.Count(i => i.IsFolderLike).ToString(CultureInfo.CurrentCulture)));
            DetailsRows.Add(new InfoRow("Files", _selection.Count(i => !i.IsFolderLike).ToString(CultureInfo.CurrentCulture)));
            var risky = _selection.Count(i => i.IsRisky);
            if (risky > 0)
            {
                DetailsWarning = $"{Plural(risky, "selected item")} can run code. Only open them if you trust the source.";
            }
        }
        else if (_session is { } session && IsInArchive)
        {
            DescribeArchive(session);
        }
        else
        {
            DescribeLocation();
        }
    }

    private void DescribeArchive(ArchiveSession session)
    {
        var info = session.Info;
        DetailsTitle = Path.GetFileName(session.Location.DisplayRoot);
        DetailsSubtitle = $"{info.Format.DisplayName} archive";
        DetailsKind = FileKind.Archive;
        var entries = session.Reader.Entries;
        var files = entries.Count(e => !e.IsDirectory);
        var unpacked = entries.Aggregate(0UL, (sum, e) => sum + (e.Size ?? 0));

        DetailsRows.Add(new InfoRow("Files", files.ToString("N0", CultureInfo.CurrentCulture)));
        DetailsRows.Add(new InfoRow("Unpacked size", ByteSize.Format(unpacked)));
        if (info.PhysicalSize is { } physical)
        {
            DetailsRows.Add(new InfoRow("Archive size", ByteSize.Format(physical)));
            DetailsRows.Add(new InfoRow("Ratio", ByteSize.FormatRatio(physical, unpacked)));
        }

        DetailsRows.Add(new InfoRow("Encryption", DescribeEncryption(session)));
        if (info.IsSolid)
        {
            DetailsRows.Add(new InfoRow("Solid", "Yes"));
        }

        if (info.IsMultiVolume)
        {
            DetailsRows.Add(new InfoRow("Volumes", info.VolumeCount.ToString(CultureInfo.CurrentCulture)));
        }

        if (!string.IsNullOrWhiteSpace(info.Method))
        {
            DetailsRows.Add(new InfoRow("Method", info.Method));
        }

        DetailsRows.Add(new InfoRow("Origin", session.CameFromInternet ? "Downloaded from the internet" : "Local", IsWarning: session.CameFromInternet));
        if (!string.IsNullOrWhiteSpace(info.Comment))
        {
            DetailsRows.Add(new InfoRow("Comment", info.Comment.Trim()));
        }

        if (info.Errors != ArchiveIssues.None)
        {
            DetailsWarning = DescribeIssues(info);
        }
    }

    private void DescribeLocation()
    {
        switch (CurrentLocation)
        {
            case FolderLocation folder:
                DetailsTitle = Path.GetFileName(folder.Path.TrimEnd('\\')) is { Length: > 0 } name ? name : folder.Path;
                DetailsSubtitle = "Folder";
                DetailsKind = FileKind.Folder;
                DetailsRows.Add(new InfoRow("Items", (_items.Count - 1).ToString("N0", CultureInfo.CurrentCulture)));
                if (Core.Platform.FileSystemInfoEx.GetFreeSpace(folder.Path) is { } free)
                {
                    DetailsRows.Add(new InfoRow("Free space", ByteSize.Format(free)));
                }

                break;

            default:
                DetailsTitle = "This PC";
                DetailsSubtitle = $"{_items.Count} drives";
                DetailsKind = FileKind.DiskImage;
                break;
        }
    }

    private void DescribeItem(BrowserItem item)
    {
        DetailsTitle = item.Name;
        DetailsSubtitle = item.TypeName;
        DetailsKind = item.Type == BrowserItemType.Drive ? FileKind.DiskImage : item.Kind;

        if (item.Type == BrowserItemType.Drive)
        {
            DetailsRows.Add(new InfoRow("Space", item.DriveDetail ?? string.Empty));
            return;
        }

        if (item.Size is { } size && item.Type != BrowserItemType.Folder)
        {
            DetailsRows.Add(new InfoRow("Size", $"{ByteSize.Format(size)} ({ByteSize.FormatExact(size)} bytes)"));
        }

        if (item.IsInArchive)
        {
            if (item.PackedSize is { } packed)
            {
                DetailsRows.Add(new InfoRow("Packed", $"{ByteSize.Format(packed)} · {item.RatioText}"));
            }

            if (item.Node?.Entry is { } entry)
            {
                if (entry.Crc is not null)
                {
                    DetailsRows.Add(new InfoRow("CRC32", item.CrcText));
                }

                if (!string.IsNullOrWhiteSpace(entry.Method))
                {
                    DetailsRows.Add(new InfoRow("Method", entry.Method));
                }

                if (entry.IsLink)
                {
                    DetailsRows.Add(new InfoRow("Link to", entry.LinkTarget ?? "unknown", IsWarning: true));
                }
            }

            if (item.Type == BrowserItemType.ArchiveFolder && item.Node is { } node)
            {
                DetailsRows.Add(new InfoRow("Contains", Plural(node.FileCount, "file")));
            }

            DetailsRows.Add(new InfoRow("Encrypted", item.IsEncrypted ? "Yes" : "No"));
            DetailsRows.Add(new InfoRow("Path in archive", item.Node?.Path ?? item.Name));
        }
        else if (item.FullPath is { } path)
        {
            DetailsRows.Add(new InfoRow("Location", Path.GetDirectoryName(path) ?? path));
            if (item.Type == BrowserItemType.File && MarkOfTheWeb.Read(path) is not null)
            {
                DetailsRows.Add(new InfoRow("Origin", "Downloaded from the internet", IsWarning: true));
            }
        }

        if (item.Modified is not null)
        {
            DetailsRows.Add(new InfoRow("Modified", item.ModifiedText));
        }

        if (item.IsRisky)
        {
            DetailsWarning = $"{item.RiskText}. Only open it if you trust where it came from.";
        }
    }

    private void StartPreview(BrowserItem item)
    {
        if (!_services.Settings.ShowPreviews || item.IsFolderLike)
        {
            return;
        }

        var extension = Path.GetExtension(item.Name);
        var isImage = ImageExtensions.Contains(extension);
        var isText = item.Kind is FileKind.Text or FileKind.Code;
        if (!isImage && !isText)
        {
            return;
        }

        var limit = isImage ? PreviewImageLimit : PreviewTextLimit;
        if (isImage && item.Size is { } size && (ulong)limit < size)
        {
            PreviewNote = "Too large to preview.";
            return;
        }

        if (item.IsInArchive && item.IsEncrypted && _session?.Reader.Password is null)
        {
            PreviewNote = "Encrypted. Preview is available after the password has been entered.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        PreviewNote = "Loading preview…";
        _ = LoadPreviewAsync(item, isImage, (int)limit, cancellation.Token);
    }

    private async Task LoadPreviewAsync(BrowserItem item, bool isImage, int limit, CancellationToken cancellationToken)
    {
        try
        {
            var session = _session;
            var bytes = await Task.Run(() => item.IsInArchive
                ? ReadArchiveEntry(session!, item.Node!.Entry!, limit, cancellationToken)
                : ReadFileHead(item.FullPath!, limit), cancellationToken);

            if (cancellationToken.IsCancellationRequested || bytes is null)
            {
                return;
            }

            if (isImage)
            {
                PreviewImage = await Task.Run(() => DecodeImage(bytes), cancellationToken);
                PreviewNote = PreviewImage is null ? "This image can't be previewed." : null;
            }
            else
            {
                PreviewText = DecodeText(bytes);
                PreviewNote = PreviewText is null ? "Binary content — no text preview." : bytes.Length >= limit ? "Showing the beginning of the file." : null;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArchiveException or NotSupportedException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                PreviewNote = "Preview unavailable.";
            }
        }
    }

    private static byte[]? ReadArchiveEntry(ArchiveSession session, ArchiveEntry entry, int limit, CancellationToken cancellationToken)
    {
        var target = new MemoryExtractTarget(limit);
        session.Reader.Extract([entry.Index], target, null, cancellationToken);
        return target.Data;
    }

    private static byte[] ReadFileHead(string path, int limit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[(int)Math.Min(stream.Length, limit)];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static BitmapSource? DecodeImage(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 560;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private static string? DecodeText(byte[] bytes)
    {
        var probe = bytes.AsSpan(0, Math.Min(bytes.Length, 4096));
        var hasBom = probe.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || probe.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]);
        if (!hasBom && probe.Contains((byte)0))
        {
            return null;
        }

        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return text.Length > 20_000 ? text[..20_000] : text;
    }

    private void CancelPreview()
    {
        _previewCancellation?.Cancel();
        _previewCancellation = null;
    }

    // ---------------------------------------------------------------- Helpers

    private void CloseSession()
    {
        if (_session is null)
        {
            return;
        }

        CancelPreview();
        var closing = _session;
        _session = null;
        OnPropertyChanged(nameof(Session));
        _ = Task.Run(closing.Dispose);
    }

    private void WatchFolder(FolderLocation? folder)
    {
        _watcher?.Dispose();
        _watcher = null;
        if (folder is null)
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(folder.Path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            FileSystemEventHandler changed = (_, _) => _owner.Dispatcher.BeginInvoke(() =>
            {
                _refreshDebounce.Stop();
                _refreshDebounce.Start();
            });
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Changed += changed;
            _watcher.Renamed += (s, e) => changed(s, e);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
        {
            _watcher = null;
        }
    }

    private void OpenWithShell(string path)
    {
        try
        {
            Shell.Open(path);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            MessageDialog.Error(_owner, "Couldn't open the file", ex.Message);
        }
    }

    private void SyncRecent()
    {
        RecentArchives.Clear();
        foreach (var path in _services.Settings.RecentArchives)
        {
            RecentArchives.Add(path);
        }
    }

    private static (string Archive, string Inner)? FindArchiveInPath(string path)
    {
        var current = path;
        while (Path.GetDirectoryName(current) is { } parent)
        {
            if (File.Exists(parent))
            {
                return (parent, path[(parent.Length + 1)..]);
            }

            current = parent;
        }

        return null;
    }

    private static bool IsSameArchiveFolder(Location a, Location b) =>
        a is ArchiveLocation x && b is ArchiveLocation y && x.ArchivePath == y.ArchivePath && x.InnerPath == y.InnerPath;

    private static string DescribeEncryption(ArchiveSession session)
    {
        if (session.Info.HasEncryptedHeaders)
        {
            return "Contents and file names";
        }

        var encrypted = session.Reader.Entries.Count(e => e.IsEncrypted);
        return encrypted switch
        {
            0 => "None",
            _ when encrypted == session.Reader.Entries.Count(e => !e.IsDirectory) => "All files",
            _ => $"{encrypted:N0} file(s)",
        };
    }

    private static string DescribeIssues(ArchiveInfo info)
    {
        var issues = info.Errors | info.Warnings;
        if (issues.HasFlag(ArchiveIssues.UnexpectedEnd))
        {
            return "This archive seems incomplete — it may not have finished downloading, or a volume is missing.";
        }

        if (issues.HasFlag(ArchiveIssues.HeadersError) || issues.HasFlag(ArchiveIssues.CrcError) || issues.HasFlag(ArchiveIssues.DataError))
        {
            return "Parts of this archive are damaged. Test it to see which files are affected.";
        }

        if (issues.HasFlag(ArchiveIssues.DataAfterEnd))
        {
            return "There is extra data after the end of this archive.";
        }

        return info.ErrorMessage ?? "The archive engine reported a problem with this archive.";
    }

    private static string Plural(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count.ToString("N0", CultureInfo.CurrentCulture)} {noun}s";

    public void Dispose()
    {
        _watcher?.Dispose();
        _refreshDebounce.Stop();
        CancelPreview();
        _session?.Dispose();
        _session = null;
    }

    /// <summary>Collects a single small entry in memory for previews.</summary>
    private sealed class MemoryExtractTarget(int limit) : IExtractTarget
    {
        private MemoryStream? _stream;

        public byte[]? Data { get; private set; }

        public Stream? OpenEntry(ArchiveEntry entry) => _stream = new MemoryStream();

        public long GetByteLimit(ArchiveEntry entry) => limit;

        public void CompleteEntry(ArchiveEntry entry, Stream stream, EntryResult result, long bytesWritten)
        {
            // A preview only needs the beginning; hitting the limit still yields usable bytes.
            if (result is EntryResult.Ok or EntryResult.SizeLimitExceeded && _stream is not null)
            {
                Data = _stream.ToArray();
            }

            stream.Dispose();
        }
    }
}
