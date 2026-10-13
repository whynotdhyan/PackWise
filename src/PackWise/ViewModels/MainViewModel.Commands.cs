using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PackWise.Archiving;
using PackWise.Browser;
using PackWise.Core.Extraction;
using PackWise.Core.Files;
using PackWise.Core.Platform;
using PackWise.Core.Security;
using PackWise.Core.Settings;
using PackWise.Infrastructure;
using PackWise.Views.Dialogs;
using PackWise.Views.Tools;

namespace PackWise.ViewModels;

public enum ExtractMode
{
    Dialog,
    Here,
    ToNamedFolder,
}

public sealed partial class MainViewModel
{
    // ---------------------------------------------------------------- Add

    private bool CanAdd() => CurrentLocation is FolderLocation || (_session?.CanModify ?? false);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task Add()
    {
        if (IsInArchive)
        {
            var picker = new OpenFileDialog { Title = "Add files to the archive", Multiselect = true };
            if (picker.ShowDialog(_owner) == true)
            {
                await AddToCurrentArchiveAsync(picker.FileNames);
            }

            return;
        }

        var sources = _selection.Where(i => i.Type is BrowserItemType.File or BrowserItemType.Folder).Select(i => i.FullPath!).ToList();
        if (sources.Count == 0)
        {
            MessageDialog.Info(_owner, "Select something to add", "Select the files or folders you want to put in an archive, then choose Add.");
            return;
        }

        await CreateArchiveAsync(sources, ((FolderLocation)CurrentLocation).Path);
    }

    /// <summary>Quick action: compress the selection into "&lt;name&gt;.zip" next to it without a dialog.</summary>
    [RelayCommand]
    private Task QuickZip() =>
        QuickArchiveAsync(_selection.Where(i => i.Type is BrowserItemType.File or BrowserItemType.Folder).Select(i => i.FullPath!).ToList(), "zip");

    /// <summary>Compresses files into a ZIP or 7Z next to them with default settings and no questions.</summary>
    public async Task QuickArchiveAsync(IReadOnlyList<string> sources, string format)
    {
        if (sources.Count == 0)
        {
            return;
        }

        var folder = Path.GetDirectoryName(sources[0].TrimEnd('\\')) ?? sources[0];
        var model = new AddArchiveViewModel(sources, folder, _services.Settings) { FormatName = format };
        model.ArchivePath = FileSystemInfoEx.MakeUnique(model.ArchivePath);
        await RunCreateAsync(model, sources);
    }

    [RelayCommand]
    private Task ConvertToZip() => ConvertAsync(SelectedOrCurrentArchives(), "zip");

    [RelayCommand]
    private Task ConvertTo7z() => ConvertAsync(SelectedOrCurrentArchives(), "7z");

    private List<string> SelectedOrCurrentArchives() =>
        CurrentLocation is ArchiveLocation { IsNested: false } archive
            ? [archive.ArchivePath]
            : _selection.Where(i => i.Type == BrowserItemType.File && LooksLikeArchive(i)).Select(i => i.FullPath!).ToList();

    /// <summary>
    /// Re-packs archives in another format next to the original, which is kept. Contents are
    /// extracted to private temporary space, recompressed and tested before the result appears.
    /// </summary>
    public async Task ConvertAsync(IReadOnlyList<string> archives, string format)
    {
        if (archives.Count == 0)
        {
            MessageDialog.Info(_owner, "Select an archive", "Select one or more archives to convert, then try again.");
            return;
        }

        CancelPreview();
        var target = ArchiveWriter.FindFormat(format);
        var heading = archives.Count == 1 ? $"Converting {Path.GetFileName(archives[0])} to {target.DisplayName}" : $"Converting {archives.Count} archives to {target.DisplayName}";
        var progress = new OperationProgress("Converting", heading, IconKind.Layers);
        var lines = new List<ReportLine>();
        var created = new List<string>();

        try
        {
            await Operations.RunAsync(_owner, progress, p =>
            {
                foreach (var archive in archives)
                {
                    p.Token.ThrowIfCancellationRequested();
                    try
                    {
                        created.Add(ConvertOne(archive, target, p));
                    }
                    catch (Exception ex) when (ex is ArchiveException or IOException or UnauthorizedAccessException)
                    {
                        lines.Add(new ReportLine(Path.GetFileName(archive), ex.Message));
                    }
                }

                return 0;
            });
        }
        catch (OperationCanceledException)
        {
            Banner = new Notice("Conversion canceled. The original archives were not changed.", NoticeKind.Info);
            return;
        }

        _services.Log.Info("convert", $"Converted {created.Count} archive(s) to {target.DisplayName}; {lines.Count} failed.");
        if (created.Count > 0)
        {
            _pendingSelection = Path.GetFileName(created[^1]);
        }

        await RefreshAsync();
        ReportDialog.Show(_owner, new Report
        {
            Title = lines.Count == 0 ? "Conversion complete" : "Some archives weren't converted",
            Summary = $"{Plural(created.Count, "archive")} converted to {target.DisplayName} and tested. The originals were kept. Archive comments and links inside archives are not carried over.",
            Kind = lines.Count == 0 ? MessageKind.Success : MessageKind.Warning,
            Lines = lines,
            ActionText = created.Count > 0 ? "Show in folder" : null,
            Action = created.Count > 0 ? () => Shell.Reveal(created[^1]) : null,
        });
    }

    private string ConvertOne(string archive, ArchiveFormat target, OperationProgress p)
    {
        var name = Path.GetFileName(archive);
        p.SetStage($"Opening {name}…");
        using var reader = ArchiveReader.Open(archive, new ArchiveOpenOptions
        {
            AllowUntrustedFormats = _services.Settings.AllowUntrustedFormats,
            Password = _services.Passwords.Get(archive),
            PasswordProvider = _passwordProvider,
        }, p.Token);

        var workspace = _services.Temp.CreateFolder();
        try
        {
            p.SetStage($"Unpacking {name}…");
            var plan = ExtractionPlanner.Create(reader.Entries, new ExtractionRequest { Destination = workspace, ConflictPolicy = ConflictPolicy.KeepBoth }, null, p.Token);
            var report = SafeExtraction.Execute(reader, plan, new ExtractionOptions { MarkOfTheWeb = MarkOfTheWebPolicy.Off }, p.ArchiveReporter, p.Token);
            if (!report.Succeeded)
            {
                var reason = report.Problems.FirstOrDefault(x => x.Kind == ProblemKind.Failed)?.Message;
                throw new ArchiveException(reason ?? "Some files could not be unpacked, so nothing was converted.");
            }

            var password = reader.Password;
            _services.Passwords.Set(archive, password);
            var output = FileSystemInfoEx.MakeUnique(Path.Combine(Path.GetDirectoryName(archive)!, $"{SubfolderFor(archive)}.{target.PrimaryExtension}"));

            p.SetStage($"Compressing to {Path.GetFileName(output)}…");
            var items = ArchiveSourceItem.Collect(Directory.EnumerateFileSystemEntries(workspace).ToList(), p.Token);
            var options = new CreateArchiveOptions
            {
                Format = target,
                Level = _services.Settings.DefaultLevel,
                Password = password,
                EncryptFileNames = password is not null && reader.Info.HasEncryptedHeaders && target.CanEncryptNames,
            };
            using (var pending = ArchiveWriter.Create(output, items, options, p.ArchiveReporter, p.Token))
            {
                pending.Commit();
            }

            p.SetStage($"Testing {Path.GetFileName(output)}…");
            using (var check = ArchiveReader.Open(output, new ArchiveOpenOptions { Password = password }, p.Token))
            {
                if (!check.Test(null, null, p.Token).Succeeded)
                {
                    throw new ArchiveException($"The converted archive {Path.GetFileName(output)} failed its test and should not be trusted.");
                }
            }

            if (MarkOfTheWeb.Read(archive) is { } zone)
            {
                MarkOfTheWeb.Write(output, zone);
            }

            return output;
        }
        finally
        {
            try
            {
                Directory.Delete(workspace, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public async Task CreateArchiveAsync(IReadOnlyList<string> sources, string folder)
    {
        var model = new AddArchiveViewModel(sources, folder, _services.Settings);
        var dialog = new AddArchiveDialog(model) { Owner = _owner };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (File.Exists(model.ArchivePath) && !MessageDialog.Confirm(_owner, "Replace the existing archive?",
                $"“{Path.GetFileName(model.ArchivePath)}” already exists. The old archive goes to the Recycle Bin only after the new one is complete.",
                "Replace", MessageKind.Warning))
        {
            return;
        }

        _services.Settings.DefaultFormat = model.FormatName;
        _services.Settings.DefaultLevel = model.Level;
        _services.Settings.TestAfterCreate = model.TestAfter;
        await RunCreateAsync(model, sources);
    }

    private async Task RunCreateAsync(AddArchiveViewModel model, IReadOnlyList<string> sources)
    {
        var options = model.BuildOptions();
        var path = model.ArchivePath;
        var name = Path.GetFileName(path);
        var progress = new OperationProgress("Creating archive", $"Creating {name}", IconKind.Add);

        try
        {
            var (itemCount, test) = await Operations.RunAsync(_owner, progress, p =>
            {
                p.SetStage("Collecting files…");
                var items = ArchiveSourceItem.Collect(sources, p.Token);
                p.SetStage("Compressing…");

                var replacing = File.Exists(path);
                using (var pending = ArchiveWriter.Create(path, items, options, p.ArchiveReporter, p.Token))
                {
                    if (replacing && RecycleBin.IsAvailableFor(path))
                    {
                        RecycleBin.Send([path]);
                    }

                    pending.Commit();
                }

                ArchiveRunResult? result = null;
                if (model.TestAfter)
                {
                    p.SetStage("Testing the new archive…");
                    using var reader = ArchiveReader.Open(path, new ArchiveOpenOptions { Password = options.Password });
                    result = reader.Test(null, p.ArchiveReporter, p.Token);
                }

                return (items.Count(i => !i.IsDirectory), result);
            });

            _services.Log.Info("create", $"Created {options.Format.DisplayName} archive with {itemCount} files{(options.Password is null ? string.Empty : ", encrypted")}.", path);
            if (options.Password is not null)
            {
                _services.Passwords.Set(path, options.Password);
            }

            _pendingSelection = name;
            await RefreshAsync();

            if (test is { Succeeded: false })
            {
                ShowRunReport("The new archive failed its test", path, test, MessageKind.Error);
            }
            else
            {
                var size = new FileInfo(path).Length;
                Banner = new Notice(
                    $"Created {name} · {Plural(itemCount, "file")} · {ByteSize.Format(size)}{(test is null ? string.Empty : " · tested OK")}",
                    NoticeKind.Success, "Show in folder", () => Shell.Reveal(path));
            }
        }
        catch (OperationCanceledException)
        {
            Banner = new Notice("Archive creation canceled. Nothing was written.", NoticeKind.Info);
        }
        catch (Exception ex) when (ex is ArchiveException or IOException or UnauthorizedAccessException)
        {
            _services.Log.Error("create", ex.Message, path);
            MessageDialog.Error(_owner, "Couldn't create the archive", ex.Message);
        }
    }

    public async Task AddToCurrentArchiveAsync(IReadOnlyList<string> paths)
    {
        if (_session is not { CanModify: true } session || CurrentLocation is not ArchiveLocation location)
        {
            MessageDialog.Info(_owner, "This archive can't be changed", "PackWise can add to ZIP and 7z archives that aren't split, nested or read-only.");
            return;
        }

        var prefix = location.InnerPath.Length == 0 ? string.Empty : location.InnerPath + "\\";
        var target = location.InnerPath.Length == 0 ? Path.GetFileName(session.FilePath) : $"{Path.GetFileName(session.FilePath)}\\{location.InnerPath}";
        if (!MessageDialog.Confirm(_owner, $"Add {Plural(paths.Count, "item")} to the archive?",
                $"They'll be added to {target}. Items with the same name are replaced.", "Add"))
        {
            return;
        }

        await RewriteArchiveAsync(session, "Adding files", IconKind.Add, [], paths, prefix);
    }

    // ---------------------------------------------------------------- Extract

    private bool CanExtract() => IsInArchive || _selection.Any(i => i.Type == BrowserItemType.File && LooksLikeArchive(i));

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private Task Extract() => ExtractAsync(ExtractMode.Dialog);

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private Task ExtractHere() => ExtractAsync(ExtractMode.Here);

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private Task ExtractToFolder() => ExtractAsync(ExtractMode.ToNamedFolder);

    public async Task ExtractAsync(ExtractMode mode)
    {
        CancelPreview();
        if (IsInArchive && _session is { } session && CurrentLocation is ArchiveLocation location)
        {
            await ExtractFromSessionAsync(session, location, mode);
        }
        else
        {
            var archives = _selection.Where(i => i.Type == BrowserItemType.File && LooksLikeArchive(i)).Select(i => i.FullPath!).ToList();
            if (archives.Count > 0)
            {
                await ExtractFilesAsync(archives, mode);
            }
        }
    }

    private async Task ExtractFromSessionAsync(ArchiveSession session, ArchiveLocation location, ExtractMode mode)
    {
        var selected = _selection.Where(i => i.Node is not null).ToList();
        HashSet<int>? indices = null;
        var baseFolder = string.Empty;
        IEnumerable<ArchiveEntry> entries = session.Reader.Entries;

        if (selected.Count > 0)
        {
            indices = selected.SelectMany(i => i.Node!.DescendantEntries()).Select(e => e.Index).ToHashSet();
            entries = session.Reader.Entries.Where(e => indices.Contains(e.Index));
            baseFolder = ShowLocationColumn ? string.Empty : location.InnerPath;
        }

        var archiveName = Path.GetFileName(location.DisplayRoot);
        var preview = ExtractionPreview.Build(archiveName, entries, session.CameFromInternet, baseFolder);
        var host = location.HostFolder;

        if (ResolveDestination(preview, host, mode, selected.Count > 0) is not { } choice)
        {
            return;
        }

        var progress = new OperationProgress("Extracting", $"Extracting from {archiveName}", IconKind.Extract);
        try
        {
            var report = await Operations.RunAsync(_owner, progress, p =>
            {
                p.SetStage("Checking destination…");
                var plan = ExtractionPlanner.Create(session.Reader.Entries, new ExtractionRequest
                {
                    Destination = choice.Destination,
                    SelectedIndices = indices,
                    BaseFolder = baseFolder,
                    PreservePaths = choice.PreservePaths,
                    ConflictPolicy = choice.Policy,
                }, Resolver(), p.Token);

                if (plan.Canceled)
                {
                    return null;
                }

                p.SetStage("Extracting…");
                return SafeExtraction.Execute(session.Reader, plan, ExtractionOptionsFor(session.ZoneIdentifier), p.ArchiveReporter, p.Token);
            });

            session.RememberPassword();
            FinishExtraction(report, archiveName, choice);
        }
        catch (OperationCanceledException)
        {
            Banner = new Notice("Extraction canceled. Files that finished are kept; the one in progress was removed.", NoticeKind.Info);
        }
        catch (Exception ex) when (ex is ArchiveException or IOException or UnauthorizedAccessException)
        {
            _services.Log.Error("extract", ex.Message, session.FilePath);
            MessageDialog.Error(_owner, "Extraction failed", ex.Message);
        }
    }

    public async Task ExtractFilesAsync(IReadOnlyList<string> archives, ExtractMode mode)
    {
        var folder = Path.GetDirectoryName(archives[0])!;
        DestinationChoice? choice = null;

        if (mode == ExtractMode.Dialog)
        {
            if (archives.Count == 1)
            {
                // Open once to build an accurate preview before asking where to put things.
                ArchiveSession? session = null;
                try
                {
                    var location = new ArchiveLocation(archives[0], string.Empty, archives[0], new FolderLocation(folder));
                    session = await ArchiveSession.OpenAsync(location, _passwordProvider);
                    await ExtractFromSessionAsync(session, location, mode);
                }
                catch (Exception ex) when (ex is ArchiveException or IOException or UnauthorizedAccessException)
                {
                    MessageDialog.Error(_owner, "Can't open this archive", ex.Message);
                }
                finally
                {
                    session?.Dispose();
                }

                return;
            }

            var preview = new ExtractionPreview { ArchiveName = $"{archives.Count} archives", FileCount = archives.Count };
            choice = ResolveDestination(preview, folder, mode, selectionOnly: false, batch: true);
            if (choice is null)
            {
                return;
            }
        }

        var progress = new OperationProgress("Extracting", archives.Count == 1 ? $"Extracting {Path.GetFileName(archives[0])}" : $"Extracting {archives.Count} archives", IconKind.Extract);
        var results = new List<(string Archive, ExtractionReport? Report, string? Error)>();

        try
        {
            await Operations.RunAsync(_owner, progress, p =>
            {
                for (var i = 0; i < archives.Count; i++)
                {
                    p.Token.ThrowIfCancellationRequested();
                    var archive = archives[i];
                    var counter = archives.Count == 1 ? string.Empty : $"{i + 1} of {archives.Count} · ";
                    p.SetStage($"{counter}Opening {Path.GetFileName(archive)}…");
                    try
                    {
                        using var reader = ArchiveReader.Open(archive, new ArchiveOpenOptions
                        {
                            AllowUntrustedFormats = _services.Settings.AllowUntrustedFormats,
                            Password = _services.Passwords.Get(archive),
                            PasswordProvider = _passwordProvider,
                        }, p.Token);

                        var preview = ExtractionPreview.Build(Path.GetFileName(archive), reader.Entries, false);
                        var destination = choice is not null
                            ? Path.Combine(choice.Destination, SubfolderFor(archive))
                            : mode == ExtractMode.ToNamedFolder || !preview.HasSingleRoot
                                ? Path.Combine(Path.GetDirectoryName(archive)!, SubfolderFor(archive))
                                : Path.GetDirectoryName(archive)!;

                        var plan = ExtractionPlanner.Create(reader.Entries, new ExtractionRequest
                        {
                            Destination = destination,
                            ConflictPolicy = choice?.Policy ?? _services.Settings.ConflictPolicy,
                            PreservePaths = choice?.PreservePaths ?? true,
                        }, Resolver(), p.Token);

                        if (plan.Canceled)
                        {
                            return 0;
                        }

                        p.SetStage($"{counter}Extracting {Path.GetFileName(archive)}…");
                        var report = SafeExtraction.Execute(reader, plan, ExtractionOptionsFor(MarkOfTheWeb.Read(archive)), p.ArchiveReporter, p.Token);
                        _services.Passwords.Set(archive, reader.Password);
                        results.Add((archive, report, null));
                    }
                    catch (Exception ex) when (ex is ArchiveException or IOException or UnauthorizedAccessException)
                    {
                        results.Add((archive, null, ex.Message));
                    }
                }

                return 0;
            });
        }
        catch (OperationCanceledException)
        {
            Banner = new Notice("Extraction canceled.", NoticeKind.Info);
            return;
        }

        await RefreshAsync();
        if (results.Count == 1 && results[0].Report is { } single)
        {
            FinishExtraction(single, Path.GetFileName(results[0].Archive), choice ?? new DestinationChoice(single.Destination, ConflictPolicy.Ask, true, _services.Settings.OpenFolderAfterExtract));
            return;
        }

        var lines = results
            .Where(r => r.Error is not null || r.Report is { Succeeded: false })
            .Select(r => new ReportLine(Path.GetFileName(r.Archive), r.Error ?? Summarize(r.Report!)))
            .ToList();
        var written = results.Sum(r => r.Report?.Written ?? 0);
        ReportDialog.Show(_owner, new Report
        {
            Title = lines.Count == 0 ? "Extraction complete" : "Extraction finished with problems",
            Summary = $"{Plural(written, "file")} extracted from {Plural(results.Count, "archive")}.",
            Kind = lines.Count == 0 ? MessageKind.Success : MessageKind.Warning,
            Lines = lines,
            ActionText = "Open folder",
            Action = () => Shell.Open(choice?.Destination ?? folder),
        });
    }

    private sealed record DestinationChoice(string Destination, ConflictPolicy Policy, bool PreservePaths, bool OpenWhenDone);

    private DestinationChoice? ResolveDestination(ExtractionPreview preview, string host, ExtractMode mode, bool selectionOnly, bool batch = false)
    {
        var settings = _services.Settings;
        switch (mode)
        {
            case ExtractMode.Here:
                var here = preview.HasSingleRoot || (preview.FileCount <= 1 && !batch) ? host : Path.Combine(host, SubfolderFor(preview.ArchiveName));
                return new DestinationChoice(here, settings.ConflictPolicy, true, false);

            case ExtractMode.ToNamedFolder:
                return new DestinationChoice(Path.Combine(host, SubfolderFor(preview.ArchiveName)), settings.ConflictPolicy, true, false);
        }

        var model = new ExtractViewModel(preview, host, settings, selectionOnly);
        if (batch)
        {
            model.CreateSubfolder = false;
        }

        var dialog = new ExtractDialog(model) { Owner = _owner };
        return dialog.ShowDialog() == true
            ? new DestinationChoice(model.FinalDestination, model.Policy, model.PreservePaths, model.OpenWhenDone)
            : null;
    }

    private void FinishExtraction(ExtractionReport? report, string archiveName, DestinationChoice choice)
    {
        if (report is null)
        {
            Banner = new Notice("Extraction stopped before anything was written.", NoticeKind.Info);
            return;
        }

        _services.Log.Info("extract", $"{report.Written} written, {report.Skipped} skipped, {report.Blocked} blocked, {report.Failed} failed.", report.Destination);

        if (report.Succeeded && report.Blocked == 0 && report.Damaged == 0)
        {
            Banner = new Notice($"Extracted {Plural(report.Written, "file")} from {archiveName}", NoticeKind.Success, "Open folder", () => Shell.Open(report.Destination));
            if (choice.OpenWhenDone)
            {
                OpenWithShell(report.Destination);
            }

            return;
        }

        var kind = report.Failed > 0 || report.LimitExceeded ? MessageKind.Error : MessageKind.Warning;
        var title = report.PasswordCanceled ? "Extraction stopped: password needed"
            : report.LimitExceeded ? "Extraction stopped: output limit reached"
            : report.Canceled ? "Extraction canceled"
            : report.Failed > 0 ? "Some files couldn't be extracted"
            : "Extraction finished with notes";

        ReportDialog.Show(_owner, new Report
        {
            Title = title,
            Summary = Summarize(report),
            Kind = kind,
            Lines = report.Problems.Select(p => new ReportLine(p.ArchivePath.Length == 0 ? archiveName : p.ArchivePath, p.Message)).ToList(),
            ActionText = report.Written > 0 ? "Open folder" : null,
            Action = () => Shell.Open(report.Destination),
        });
    }

    private static string Summarize(ExtractionReport report)
    {
        var parts = new List<string> { $"{Plural(report.Written, "file")} extracted" };
        if (report.Skipped > 0)
        {
            parts.Add($"{report.Skipped:N0} skipped");
        }

        if (report.Blocked > 0)
        {
            parts.Add($"{report.Blocked:N0} blocked for safety");
        }

        if (report.Failed > 0)
        {
            parts.Add($"{report.Failed:N0} failed");
        }

        if (report.Damaged > 0)
        {
            parts.Add($"{report.Damaged:N0} kept despite errors");
        }

        if (report.LimitExceeded)
        {
            parts.Add("stopped because the output grew beyond the available space");
        }

        return string.Join(" · ", parts) + ".";
    }

    private ExtractionOptions ExtractionOptionsFor(string? zoneIdentifier) => new()
    {
        MarkOfTheWeb = _services.Settings.MarkOfTheWeb,
        ZoneIdentifier = zoneIdentifier,
        KeepDamagedFiles = _services.Settings.KeepDamagedFiles,
        RecycleReplacedFiles = _services.Settings.RecycleReplacedFiles,
    };

    private UiConflictResolver Resolver() => new(WindowHelper.ActiveOrMain, _services.Settings.RecycleReplacedFiles);

    private static string SubfolderFor(string archive)
    {
        var name = Path.GetFileName(archive);
        var stem = Path.GetFileNameWithoutExtension(name);
        foreach (var inner in new[] { ".tar", ".part1", ".part01", ".part001", ".7z", ".zip" })
        {
            if (stem.EndsWith(inner, StringComparison.OrdinalIgnoreCase))
            {
                stem = stem[..^inner.Length];
            }
        }

        return EntryPathSanitizer.SanitizeFileName(stem.Length == 0 ? "Extracted" : stem);
    }

    // ---------------------------------------------------------------- Test

    private bool CanTest() => IsInArchive || _selection.Any(i => i.Type == BrowserItemType.File && LooksLikeArchive(i));

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task Test()
    {
        CancelPreview();
        if (IsInArchive && _session is { } session)
        {
            var indices = _selection.Where(i => i.Node is not null).SelectMany(i => i.Node!.DescendantEntries()).Select(e => e.Index).ToHashSet();
            var progress = new OperationProgress("Testing", $"Testing {Path.GetFileName(session.Location.DisplayRoot)}", IconKind.Test);
            try
            {
                var result = await Operations.RunAsync(_owner, progress, p => session.Reader.Test(indices.Count > 0 ? indices : null, p.ArchiveReporter, p.Token));
                session.RememberPassword();
                ShowRunReport(result.Succeeded ? "No errors found" : "Problems found", session.Location.DisplayRoot, result, result.Succeeded ? MessageKind.Success : MessageKind.Error);
            }
            catch (OperationCanceledException)
            {
                Banner = new Notice("Test canceled.", NoticeKind.Info);
            }

            return;
        }

        await TestFilesAsync(_selection.Where(i => i.Type == BrowserItemType.File && LooksLikeArchive(i)).Select(i => i.FullPath!).ToList());
    }

    public async Task TestFilesAsync(IReadOnlyList<string> archives)
    {
        if (archives.Count == 0)
        {
            return;
        }

        var batch = new OperationProgress("Testing", archives.Count == 1 ? $"Testing {Path.GetFileName(archives[0])}" : $"Testing {archives.Count} archives", IconKind.Test);
        var lines = new List<ReportLine>();
        var tested = 0;
        try
        {
            await Operations.RunAsync(_owner, batch, p =>
            {
                foreach (var archive in archives)
                {
                    p.Token.ThrowIfCancellationRequested();
                    p.SetStage(Path.GetFileName(archive));
                    try
                    {
                        using var reader = ArchiveReader.Open(archive, new ArchiveOpenOptions
                        {
                            AllowUntrustedFormats = _services.Settings.AllowUntrustedFormats,
                            Password = _services.Passwords.Get(archive),
                            PasswordProvider = _passwordProvider,
                        }, p.Token);
                        var result = reader.Test(null, p.ArchiveReporter, p.Token);
                        _services.Passwords.Set(archive, reader.Password);
                        tested++;
                        foreach (var outcome in result.Outcomes.Where(o => !o.Result.IsSuccess()))
                        {
                            lines.Add(new ReportLine($"{Path.GetFileName(archive)} › {outcome.Entry.Path}", outcome.Result.Describe()));
                        }

                        if (result.PasswordCanceled)
                        {
                            lines.Add(new ReportLine(Path.GetFileName(archive), "Not tested: password needed."));
                        }
                    }
                    catch (Exception ex) when (ex is ArchiveException or IOException or UnauthorizedAccessException)
                    {
                        lines.Add(new ReportLine(Path.GetFileName(archive), ex.Message));
                    }
                }

                return 0;
            });
        }
        catch (OperationCanceledException)
        {
            Banner = new Notice("Test canceled.", NoticeKind.Info);
            return;
        }

        ReportDialog.Show(_owner, new Report
        {
            Title = lines.Count == 0 ? "No errors found" : "Problems found",
            Summary = lines.Count == 0 ? $"{Plural(tested, "archive")} tested. Every file matched its checksum." : $"{Plural(lines.Count, "problem")} in {Plural(archives.Count, "archive")}.",
            Kind = lines.Count == 0 ? MessageKind.Success : MessageKind.Error,
            Lines = lines,
        });
    }

    private void ShowRunReport(string title, string archivePath, ArchiveRunResult result, MessageKind kind)
    {
        var failures = result.Outcomes.Where(o => !o.Result.IsSuccess()).Select(o => new ReportLine(o.Entry.Path, o.Result.Describe())).ToList();
        var tested = result.Outcomes.Count(o => !o.Entry.IsDirectory);
        var summary = result.PasswordCanceled ? "Testing stopped because a password is needed."
            : failures.Count == 0 ? $"{Plural(tested, "file")} tested in {Path.GetFileName(archivePath)}. Every file matched its checksum."
            : $"{Plural(failures.Count, "file")} failed. PackWise does not repair archives; try downloading the file again or extracting what is intact.";

        _services.Log.Info("test", summary, archivePath);
        ReportDialog.Show(_owner, new Report { Title = title, Summary = summary, Kind = result.PasswordCanceled ? MessageKind.Warning : kind, Lines = failures });
    }

    // ---------------------------------------------------------------- View

    private bool CanView() => _selection.Count == 1 && !_selection[0].IsFolderLike;

    [RelayCommand(CanExecute = nameof(CanView))]
    private Task View() => OpenItemAsync(_selection[0]);

    private async Task ViewArchiveItemAsync(BrowserItem item)
    {
        if (_session is not { } session || item.Node?.Entry is not { } entry)
        {
            return;
        }

        if (item.IsRisky && _services.Settings.WarnBeforeOpeningRiskyFiles &&
            !MessageDialog.Confirm(_owner, $"Open “{item.Name}”?",
                $"{item.RiskText}. Files like this can change your PC, so open it only if you trust where the archive came from.",
                "Open anyway", MessageKind.Warning, dangerous: true))
        {
            return;
        }

        var path = await ExtractToTempAsync(session, new HashSet<int> { entry.Index }, item.Name);
        if (path is null)
        {
            return;
        }

        OpenWithShell(path);
        Banner = new Notice($"Opened a temporary copy of {item.Name}. Changes you make there are not saved into the archive.", NoticeKind.Info);
    }

    private async Task OpenNestedArchiveAsync(BrowserItem item)
    {
        if (_session is not { } session || item.Node?.Entry is not { } entry || CurrentLocation is not ArchiveLocation location)
        {
            return;
        }

        var path = await ExtractToTempAsync(session, new HashSet<int> { entry.Index }, item.Name);
        if (path is not null)
        {
            await OpenArchiveAsync(path, location);
        }
    }

    private async Task<string?> ExtractToTempAsync(ArchiveSession session, IReadOnlySet<int> indices, string displayName)
    {
        CancelPreview();
        var folder = _services.Temp.CreateFolder();
        var progress = new OperationProgress("Opening", $"Preparing {displayName}", IconKind.View);
        try
        {
            var report = await Operations.RunAsync(_owner, progress, p =>
            {
                var plan = ExtractionPlanner.Create(session.Reader.Entries, new ExtractionRequest
                {
                    Destination = folder,
                    SelectedIndices = indices,
                    PreservePaths = false,
                    ConflictPolicy = ConflictPolicy.KeepBoth,
                }, null, p.Token);
                return SafeExtraction.Execute(session.Reader, plan, ExtractionOptionsFor(session.ZoneIdentifier), p.ArchiveReporter, p.Token);
            });

            session.RememberPassword();
            if (report.WrittenFiles.Count == 0)
            {
                MessageDialog.Error(_owner, $"Couldn't open {displayName}", report.Problems.FirstOrDefault()?.Message ?? "The file could not be extracted.");
                return null;
            }

            return report.WrittenFiles[0];
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is ArchiveException or IOException or UnauthorizedAccessException)
        {
            MessageDialog.Error(_owner, $"Couldn't open {displayName}", ex.Message);
            return null;
        }
    }

    // ---------------------------------------------------------------- Delete

    private bool CanDelete() => _selection.Count > 0 && _selection.All(i => i.Type is BrowserItemType.File or BrowserItemType.Folder or BrowserItemType.ArchiveFile or BrowserItemType.ArchiveFolder);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        CancelPreview();
        if (IsInArchive)
        {
            if (_session is not { CanModify: true } session)
            {
                MessageDialog.Info(_owner, "This archive can't be changed", "PackWise can remove entries from ZIP and 7z archives that aren't split or nested.");
                return;
            }

            var indices = _selection.Where(i => i.Node is not null).SelectMany(i => i.Node!.DescendantEntries()).Select(e => e.Index).ToHashSet();
            if (!MessageDialog.Confirm(_owner, $"Delete {Plural(_selection.Count, "item")} from the archive?",
                    "PackWise writes a new copy without these items, then replaces the archive. This can't be undone from PackWise.",
                    "Delete", MessageKind.Warning, dangerous: true))
            {
                return;
            }

            await RewriteArchiveAsync(session, "Deleting from archive", IconKind.Delete, indices, [], string.Empty);
            return;
        }

        var paths = _selection.Where(i => i.Type is BrowserItemType.File or BrowserItemType.Folder).Select(i => i.FullPath!).ToList();
        if (paths.Count == 0)
        {
            return;
        }

        if (!RecycleBin.IsAvailableFor(paths[0]))
        {
            MessageDialog.Info(_owner, "No Recycle Bin on this drive",
                "PackWise only deletes by moving items to the Recycle Bin, and this drive doesn't have one. Delete these items in File Explorer if you're sure.");
            return;
        }

        if (!MessageDialog.Confirm(_owner, $"Move {Plural(paths.Count, "item")} to the Recycle Bin?",
                paths.Count == 1 ? Path.GetFileName(paths[0]) : "You can restore them from the Recycle Bin.", "Move to Recycle Bin"))
        {
            return;
        }

        try
        {
            var ok = await Task.Run(() => RecycleBin.Send(paths));
            _services.Log.Info("delete", $"Moved {paths.Count} item(s) to the Recycle Bin.", Path.GetDirectoryName(paths[0]));
            if (!ok)
            {
                MessageDialog.Error(_owner, "Some items weren't moved", "Windows couldn't move every item to the Recycle Bin. They may be open in another app.");
            }
        }
        catch (IOException ex)
        {
            MessageDialog.Error(_owner, "Couldn't delete", ex.Message);
        }

        await RefreshAsync();
    }

    private async Task RewriteArchiveAsync(ArchiveSession session, string title, IconKind icon, IReadOnlyCollection<int> remove, IReadOnlyList<string> add, string prefix)
    {
        var progress = new OperationProgress(title, $"{title}: {Path.GetFileName(session.FilePath)}", icon);
        try
        {
            await Operations.RunAsync(_owner, progress, p =>
            {
                p.SetStage("Collecting files…");
                var items = ArchiveSourceItem.Collect(add, p.Token).Select(i => i with { ArchivePath = prefix + i.ArchivePath }).ToList();
                p.SetStage("Writing a new copy of the archive…");
                using var pending = ArchiveWriter.Rewrite(session.Reader, remove, items, p.ArchiveReporter, p.Token);
                session.Reader.Dispose();
                pending.Commit();
                return 0;
            });

            _services.Log.Info("update", $"{title}: removed {remove.Count}, added {add.Count}.", session.FilePath);
            Banner = new Notice(remove.Count > 0 ? "Items deleted from the archive." : "Files added to the archive.", NoticeKind.Success);
        }
        catch (OperationCanceledException)
        {
            Banner = new Notice("Canceled. The archive was not changed.", NoticeKind.Info);
        }
        catch (Exception ex) when (ex is ArchiveException or IOException or UnauthorizedAccessException)
        {
            _services.Log.Error("update", ex.Message, session.FilePath);
            MessageDialog.Error(_owner, "The archive was not changed", ex.Message);
        }
        finally
        {
            try
            {
                await session.ReloadAsync(_passwordProvider);
            }
            catch (Exception ex) when (ex is ArchiveException or IOException)
            {
                MessageDialog.Error(_owner, "Couldn't reopen the archive", ex.Message);
            }
        }

        await RefreshAsync();
    }

    // ---------------------------------------------------------------- Info

    [RelayCommand]
    private async Task Info()
    {
        if (IsInArchive && _session is { } session)
        {
            ShowArchiveInfo(session);
            return;
        }

        if (_selection.Count != 1 || _selection[0].FullPath is not { } path)
        {
            ToggleDetails();
            return;
        }

        var item = _selection[0];
        if (item.Type == BrowserItemType.File && LooksLikeArchive(item))
        {
            var location = new ArchiveLocation(path, string.Empty, path, CurrentLocation);
            try
            {
                using var opened = await ArchiveSession.OpenAsync(location, _passwordProvider);
                ShowArchiveInfo(opened);
            }
            catch (Exception ex) when (ex is ArchiveException or IOException or UnauthorizedAccessException)
            {
                MessageDialog.Error(_owner, "Can't read this archive", ex.Message);
            }

            return;
        }

        ShowFileInfo(item, path);
    }

    private void ShowArchiveInfo(ArchiveSession session)
    {
        var info = session.Info;
        var entries = session.Reader.Entries;
        var unpacked = entries.Aggregate(0UL, (sum, e) => sum + (e.Size ?? 0));
        var general = new List<InfoRow>
        {
            new("Format", string.Join(" › ", info.FormatChain.Select(f => f.DisplayName))),
            new("Files", entries.Count(e => !e.IsDirectory).ToString("N0", CultureInfo.CurrentCulture)),
            new("Folders", entries.Count(e => e.IsDirectory).ToString("N0", CultureInfo.CurrentCulture)),
            new("Unpacked size", $"{ByteSize.Format(unpacked)} ({ByteSize.FormatExact(unpacked)} bytes)"),
        };

        if (info.PhysicalSize is { } physical)
        {
            general.Add(new("Archive size", $"{ByteSize.Format(physical)} · {ByteSize.FormatRatio(physical, unpacked)} of original"));
        }

        if (!string.IsNullOrWhiteSpace(info.Method))
        {
            general.Add(new("Compression", info.Method));
        }

        general.Add(new("Solid", info.IsSolid ? "Yes" : "No"));
        if (info.IsMultiVolume)
        {
            general.Add(new("Volumes", info.VolumeCount.ToString(CultureInfo.CurrentCulture)));
        }

        var links = entries.Count(e => e.IsLink);
        var risky = entries.Count(e => !e.IsDirectory && FileRiskClassifier.CanRunCode(FileRiskClassifier.Classify(e.Name)));
        var unsafePaths = entries.Count(e => EntryPathSanitizer.Sanitize(e.Path).IsRejected);
        var security = new List<InfoRow>
        {
            new("Encryption", DescribeEncryption(session)),
            new("Origin", session.CameFromInternet ? "Downloaded from the internet. Extracted files keep this mark." : "No internet mark", IsWarning: session.CameFromInternet),
            new("Links inside", links > 0 ? $"{links:N0} (never created on extraction)" : "None", IsWarning: links > 0),
            new("Programs and scripts", risky > 0 ? risky.ToString("N0", CultureInfo.CurrentCulture) : "None", IsWarning: risky > 0),
            new("Unsafe paths", unsafePaths > 0 ? $"{unsafePaths:N0} (blocked on extraction)" : "None", IsWarning: unsafePaths > 0),
        };

        var format = info.Format;
        var support = new List<InfoRow>
        {
            new("Open and extract", "Yes", IsGood: true),
            new("Modify in PackWise", session.CanModify ? "Yes" : "No"),
            new("Create new", format.CanCreate ? "Yes" : "No"),
            new("Encryption", format.CanCreateEncrypted ? "Read and create" : format.CanReadEncrypted ? "Read only" : "Not supported"),
            new("Engine", $"7-Zip {ArchiveReader.EngineVersion}"),
        };

        var integrity = new List<InfoRow>();
        if (info.Errors != ArchiveIssues.None || info.Warnings != ArchiveIssues.None)
        {
            integrity.Add(new("Status", DescribeIssues(info), IsWarning: true));
        }

        if (!string.IsNullOrWhiteSpace(info.Comment))
        {
            integrity.Add(new("Comment", info.Comment.Trim()));
        }

        InfoDialog.Show(_owner, Path.GetFileName(session.Location.DisplayRoot), session.Location.DisplayRoot, FileKind.Archive,
        [
            new InfoSection("GENERAL", general),
            new InfoSection("SAFETY", security),
            new InfoSection("NOTES", integrity),
            new InfoSection("WHAT PACKWISE CAN DO WITH THIS FORMAT", support),
        ], "Test archive", () => TestCommand.Execute(null));
    }

    private void ShowFileInfo(BrowserItem item, string path)
    {
        var rows = new List<InfoRow> { new("Type", item.TypeName), new("Location", Path.GetDirectoryName(path) ?? path) };
        try
        {
            if (item.Type == BrowserItemType.File)
            {
                var file = new FileInfo(path);
                rows.Add(new("Size", $"{ByteSize.Format(file.Length)} ({ByteSize.FormatExact((ulong)file.Length)} bytes)"));
                rows.Add(new("Created", file.CreationTime.ToString("g", CultureInfo.CurrentCulture)));
                rows.Add(new("Modified", file.LastWriteTime.ToString("g", CultureInfo.CurrentCulture)));
                rows.Add(new("Attributes", file.Attributes.ToString()));
                rows.Add(new("Origin", MarkOfTheWeb.Read(path) is null ? "No internet mark" : "Downloaded from the internet", IsWarning: MarkOfTheWeb.Read(path) is not null));
            }
            else
            {
                var directory = new DirectoryInfo(path);
                rows.Add(new("Created", directory.CreationTime.ToString("g", CultureInfo.CurrentCulture)));
                rows.Add(new("Modified", directory.LastWriteTime.ToString("g", CultureInfo.CurrentCulture)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            rows.Add(new("Error", ex.Message, IsWarning: true));
        }

        if (item.IsRisky)
        {
            rows.Add(new("Caution", item.RiskText ?? string.Empty, IsWarning: true));
        }

        InfoDialog.Show(_owner, item.Name, path, item.Kind, [new InfoSection("DETAILS", rows)],
            item.Type == BrowserItemType.File ? "Compute checksums" : null,
            item.Type == BrowserItemType.File ? () => OpenChecksums([path]) : null);
    }

    // ---------------------------------------------------------------- Tools and app commands

    [RelayCommand]
    private void OpenDuplicateFinder()
    {
        var roots = _selection.Where(i => i.Type is BrowserItemType.Folder or BrowserItemType.Drive).Select(i => i.FullPath!).ToList();
        if (roots.Count == 0 && CurrentLocation is FolderLocation folder)
        {
            roots.Add(folder.Path);
        }

        var window = new DuplicateFinderWindow(new DuplicateFinderViewModel(_services, roots));
        window.Show();
    }

    [RelayCommand]
    private void OpenChecksumTool()
    {
        var files = _selection.Where(i => i.Type == BrowserItemType.File).Select(i => i.FullPath!).ToList();
        OpenChecksums(files);
    }

    private static void OpenChecksums(IReadOnlyList<string> files)
    {
        var window = new ChecksumWindow(new ChecksumViewModel(files));
        window.Show();
    }

    [RelayCommand]
    private async Task OpenArchiveFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open archive",
            Filter = "Archives|*.zip;*.7z;*.rar;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.zst;*.cab;*.iso;*.wim;*.arj;*.lzh;*.001|All files|*.*",
            InitialDirectory = (CurrentLocation as FolderLocation)?.Path,
        };

        if (dialog.ShowDialog(_owner) == true)
        {
            await OpenArchiveAsync(dialog.FileName);
        }
    }

    [RelayCommand]
    private Task OpenRecent(string path) => File.Exists(path) ? OpenArchiveAsync(path) : RemoveRecent(path);

    private Task RemoveRecent(string path)
    {
        _services.Settings.RecentArchives.Remove(path);
        SyncRecent();
        MessageDialog.Info(_owner, "Archive not found", $"“{path}” has been moved or deleted.");
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ClearRecent()
    {
        _services.Settings.RecentArchives.Clear();
        SyncRecent();
    }

    [RelayCommand]
    private void AddFavorite()
    {
        var path = CurrentLocation switch
        {
            FolderLocation folder => folder.Path,
            ArchiveLocation { IsNested: false } archive => archive.ArchivePath,
            _ => null,
        };

        if (path is null || _services.Settings.Favorites.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _services.Settings.Favorites.Add(path);
        Favorites.Add(path);
        Banner = new Notice($"Added {Path.GetFileName(path.TrimEnd('\\'))} to Favorites.", NoticeKind.Success);
    }

    [RelayCommand]
    private Task OpenFavorite(string path) => NavigateToPathAsync(path);

    [RelayCommand]
    private void RemoveFavorite(string path)
    {
        _services.Settings.Favorites.RemoveAll(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
        Favorites.Remove(path);
    }

    [RelayCommand]
    private void SetTheme(string theme)
    {
        var preference = Enum.Parse<ThemePreference>(theme);
        _services.Settings.Theme = preference;
        ThemeManager.SetTheme((AppTheme)preference);
        OnPropertyChanged(nameof(ThemeName));
    }

    public string ThemeName => _services.Settings.Theme.ToString();

    [RelayCommand]
    private async Task OpenSettings()
    {
        var model = new SettingsViewModel(_services);
        var dialog = new SettingsDialog(model) { Owner = _owner };
        if (dialog.ShowDialog() == true)
        {
            model.Apply();
            _services.SaveSettings();
            OnPropertyChanged(nameof(ThemeName));
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private void ShowWelcome()
    {
        new WelcomeWindow { Owner = _owner }.ShowDialog();
        _services.Settings.HasSeenWelcome = true;
    }

    [RelayCommand]
    private void OpenGuide()
    {
        var guide = Path.Combine(AppContext.BaseDirectory, "guide", "index.html");
        if (File.Exists(guide))
        {
            OpenWithShell(guide);
        }
        else
        {
            OpenLink(Services.AppLinks.Repository + "/blob/main/docs/guide/index.html");
        }
    }

    [RelayCommand]
    private void ShowAbout() => new AboutDialog { Owner = _owner }.ShowDialog();

    [RelayCommand]
    private void ShowFormats() => new FormatSupportDialog(_services.Settings.AllowUntrustedFormats) { Owner = _owner }.ShowDialog();

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(_services.Log.Directory);
        OpenWithShell(_services.Log.Directory);
    }

    [RelayCommand]
    private void OpenLink(string url) => Shell.OpenLink(url);

    [RelayCommand]
    private void CopyPath()
    {
        var paths = _selection.Select(i => i.FullPath ?? (CurrentLocation is ArchiveLocation a ? $"{a.DisplayRoot}\\{i.Node?.Path}" : i.Name)).ToList();
        if (paths.Count == 0)
        {
            paths.Add(CurrentLocation.DisplayPath);
        }

        Shell.CopyText(string.Join(Environment.NewLine, paths));
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        var target = _selection.FirstOrDefault()?.FullPath
            ?? (CurrentLocation as FolderLocation)?.Path
            ?? (CurrentLocation is ArchiveLocation { IsNested: false } archive ? archive.ArchivePath : null);
        if (target is not null)
        {
            Shell.Reveal(target);
        }
    }

    [RelayCommand]
    private void Exit() => _owner.Close();

    // ---------------------------------------------------------------- Drag and drop

    public async Task HandleDropAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        if (IsInArchive)
        {
            await AddToCurrentArchiveAsync(paths);
            return;
        }

        if (paths.Count == 1 && File.Exists(paths[0]) && ArchiveReader.LooksLikeArchive(paths[0]))
        {
            await OpenArchiveAsync(paths[0]);
            return;
        }

        var folder = CurrentLocation is FolderLocation current ? current.Path : Path.GetDirectoryName(paths[0])!;
        await CreateArchiveAsync(paths, folder);
    }

    private static bool LooksLikeArchive(BrowserItem item) =>
        item.FullPath is { } path && (item.Kind == FileKind.Archive || ArchiveReader.LooksLikeArchive(path));
}
