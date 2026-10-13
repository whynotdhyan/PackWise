using System.Runtime.InteropServices;

namespace PackWise.Core.Platform;

public static class RecycleBin
{
    /// <summary>
    /// Only local fixed drives reliably have a Recycle Bin. On network and many removable
    /// drives Windows would delete permanently, so PackWise refuses instead.
    /// </summary>
    public static bool IsAvailableFor(string path)
    {
        var root = FileSystemInfoEx.GetDriveRoot(path);
        if (root is null || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Moves files or folders to the Recycle Bin without prompts. Returns false if any item was not recycled.</summary>
    public static bool Send(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0)
        {
            return true;
        }

        if (paths.Any(p => !IsAvailableFor(p)))
        {
            throw new IOException("The Recycle Bin is not available on this drive, so PackWise will not delete these items.");
        }

        var list = string.Join('\0', paths.Select(Path.GetFullPath)) + "\0\0";
        var from = Marshal.StringToHGlobalUni(list);
        try
        {
            var operation = new NativeMethods.ShFileOpStruct
            {
                Func = NativeMethods.FoDelete,
                From = from,
                Flags = (ushort)(NativeMethods.FofAllowUndo | NativeMethods.FofNoConfirmation | NativeMethods.FofSilent |
                                 NativeMethods.FofNoErrorUi | NativeMethods.FofWantNukeWarning),
            };

            var result = NativeMethods.SHFileOperation(ref operation);
            return result == 0 && operation.AnyOperationsAborted == 0 && paths.All(p => !File.Exists(p) && !Directory.Exists(p));
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }
}
