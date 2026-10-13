using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Northstar.Design.Branding;
using PackWise.Integration;

namespace PackWise.Views.Dialogs;

public sealed partial class WelcomeStep : ObservableObject
{
    public required string Title { get; init; }

    public required string Heading { get; init; }

    public required string Body { get; init; }

    public required IconKind Icon { get; init; }

    public IReadOnlyList<string> Tips { get; init; } = [];

    public string? ActionText { get; init; }

    public Action? Action { get; init; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

/// <summary>A short guided tour shown on first launch and from Help › Getting started.</summary>
public partial class WelcomeWindow
{
    private readonly List<WelcomeStep> _steps;
    private int _index;

    public WelcomeWindow()
    {
        InitializeComponent();
        StudioMark.Logo = MonogramLogo.Northstar;
        _steps = BuildSteps();
        StepList.ItemsSource = _steps;
        Show(0);
    }

    private static List<WelcomeStep> BuildSteps() =>
    [
        new()
        {
            Title = "Welcome",
            Heading = "Welcome to PackWise",
            Icon = IconKind.Sparkle,
            Body = "Open, create, test and clean up archives with a familiar file-manager layout and safer defaults. It's free, works offline and never asks for an account.",
            Tips =
            [
                "The toolbar works like the classic archivers you know: Add, Extract To, Test, View, Delete.",
                "Everything has a keyboard shortcut. Hover over a button to see it.",
            ],
        },
        new()
        {
            Title = "Open and browse",
            Heading = "Open an archive like a folder",
            Icon = IconKind.FolderOpen,
            Body = "Double-click a ZIP, 7Z, RAR or other archive to look inside. Move around with the breadcrumb bar, Back and Up — exactly as in a normal folder.",
            Tips =
            [
                "Select an image or text file to preview it in the details pane without extracting anything.",
                "Type in the filter box (Ctrl+F) to find files; turn on Whole archive to search every folder.",
                "Drop an archive onto the window to open it.",
            ],
        },
        new()
        {
            Title = "Extract safely",
            Heading = "Extract without surprises",
            Icon = IconKind.Extract,
            Body = "Choose Extract To (Alt+E) and PackWise shows what's inside before writing anything: how big it is, whether there's enough space, and anything that needs your attention.",
            Tips =
            [
                "Files never land outside the folder you chose, even if the archive tries.",
                "Nothing is overwritten unless you say so. Replaced files go to the Recycle Bin.",
                "Files from internet downloads keep the internet mark, so Windows can still warn you.",
            ],
        },
        new()
        {
            Title = "Create archives",
            Heading = "Pack files in two clicks",
            Icon = IconKind.Add,
            Body = "Select files or folders, then click Add (Alt+A). Pick ZIP to share with anyone, or 7Z for smaller files.",
            Tips =
            [
                "Tick Protect with a password for AES-256 encryption. Lost passwords can't be recovered — keep them safe.",
                "New archives are tested automatically, so you know they're good before you send them.",
                "Need a different format? Commands › Convert turns an archive into ZIP or 7Z.",
            ],
        },
        new()
        {
            Title = "Right-click menu",
            Heading = "PackWise in File Explorer",
            Icon = IconKind.List,
            Body = "Right-click files in File Explorer and choose PackWise to compress, extract, test, convert or check them — without opening the app. On Windows 11, choose Show more options first.",
            Tips =
            [
                "Compress to ZIP file makes one archive from everything you selected.",
                "Extract here is smart: it creates a folder only when the archive needs one.",
            ],
            ActionText = ShellIntegration.IsContextMenuRegistered() ? null : "Add PackWise to the right-click menu",
            Action = ShellIntegration.RegisterContextMenu,
        },
        new()
        {
            Title = "Clean up",
            Heading = "Find duplicates and verify files",
            Icon = IconKind.Duplicates,
            Body = "Duplicates finds files with identical contents, whatever their names, and shows how much space you could free. Checksums verifies downloads against a published SHA-256.",
            Tips =
            [
                "PackWise always keeps at least one copy, and clean-up goes to the Recycle Bin.",
                "Open this tour again any time from Help › Getting started.",
            ],
        },
    ];

    private void Show(int index)
    {
        _index = index;
        var step = _steps[index];
        foreach (var s in _steps)
        {
            s.IsCurrent = ReferenceEquals(s, step);
        }

        StepIcon.Kind = step.Icon;
        StepTitle.Text = step.Heading;
        StepBody.Text = step.Body;
        StepTips.ItemsSource = step.Tips;
        StepAction.Visibility = step.ActionText is null ? Visibility.Collapsed : Visibility.Visible;
        StepAction.Content = step.ActionText;
        StepAction.IsEnabled = true;
        BackButton.Visibility = index == 0 ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = index == _steps.Count - 1 ? "Start using PackWise" : "Next";
        SkipButton.Visibility = index == _steps.Count - 1 ? Visibility.Hidden : Visibility.Visible;
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_index == _steps.Count - 1)
        {
            Close();
        }
        else
        {
            Show(_index + 1);
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_index > 0)
        {
            Show(_index - 1);
        }
    }

    private void OnSkip(object sender, RoutedEventArgs e) => Close();

    private void OnStepAction(object sender, RoutedEventArgs e)
    {
        _steps[_index].Action?.Invoke();
        StepAction.Content = "Added — right-click any file to try it";
        StepAction.IsEnabled = false;
    }
}
