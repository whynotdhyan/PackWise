using System.Globalization;
using System.Windows;
using PackWise.Core.Extraction;
using PackWise.Core.Files;

namespace PackWise.Views.Dialogs;

public partial class ConflictDialog
{
    private ConflictChoice _choice = ConflictChoice.Cancel;

    private ConflictDialog(ConflictInfo conflict, bool recyclesReplaced)
    {
        InitializeComponent();
        Heading.Text = $"“{Path.GetFileName(conflict.TargetPath)}” already exists";
        TargetPath.Text = $"in {Path.GetDirectoryName(conflict.TargetPath)}";
        TargetPath.ToolTip = conflict.TargetPath;

        ExistingSize.Text = conflict.TargetIsDirectory ? "A folder" : conflict.ExistingSize is { } size ? ByteSize.Format(size) : "Unknown size";
        ExistingDate.Text = Format(conflict.ExistingModifiedUtc);
        IncomingSize.Text = conflict.Entry.Size is { } incoming ? ByteSize.Format(incoming) : "Unknown size";
        IncomingDate.Text = Format(conflict.Entry.Modified);
        ReplaceNote.Visibility = recyclesReplaced ? Visibility.Visible : Visibility.Collapsed;
    }

    public static ConflictResolution Ask(Window? owner, ConflictInfo conflict, bool recyclesReplaced)
    {
        var dialog = new ConflictDialog(conflict, recyclesReplaced);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        return new ConflictResolution(dialog._choice, dialog.ApplyToAll.IsChecked == true);
    }

    private static string Format(DateTime? utc) =>
        utc is { } value ? "Modified " + value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : string.Empty;

    private void OnSkip(object sender, RoutedEventArgs e) => Finish(ConflictChoice.Skip);

    private void OnReplace(object sender, RoutedEventArgs e) => Finish(ConflictChoice.Replace);

    private void OnKeepBoth(object sender, RoutedEventArgs e) => Finish(ConflictChoice.KeepBoth);

    private void OnCancel(object sender, RoutedEventArgs e) => Finish(ConflictChoice.Cancel);

    private void Finish(ConflictChoice choice)
    {
        _choice = choice;
        Close();
    }
}

/// <summary>Resolves extraction conflicts by asking the user on the UI thread.</summary>
public sealed class UiConflictResolver : IConflictResolver
{
    private readonly Func<Window?> _owner;
    private readonly bool _recyclesReplaced;

    public UiConflictResolver(Func<Window?> owner, bool recyclesReplaced)
    {
        _owner = owner;
        _recyclesReplaced = recyclesReplaced;
    }

    public ConflictResolution Resolve(ConflictInfo conflict) =>
        Application.Current.Dispatcher.Invoke(() => ConflictDialog.Ask(_owner(), conflict, _recyclesReplaced));
}
