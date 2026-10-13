using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace PackWise.Setup;

/// <summary>Choices made on the welcome page.</summary>
public sealed class InstallOptions
{
    public string Directory { get; set; } = Installer.DefaultDirectory;

    public bool ContextMenu { get; set; } = true;

    public bool DesktopShortcut { get; set; } = true;

    public bool Associations { get; set; } = true;
}

/// <summary>
/// Per-user installation: unpack the embedded app, then let PackWise.exe register its own
/// integration so install and uninstall share one implementation.
/// </summary>
public static class Installer
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NorthstarLabs.PackWise";

    public static string Version
    {
        get
        {
            var attribute = typeof(Installer).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault();
            var version = attribute?.InformationalVersion ?? "1.0.0";
            var plus = version.IndexOf('+');
            return plus > 0 ? version.Substring(0, plus) : version;
        }
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PackWise");

    /// <summary>Location of an existing installation, or null.</summary>
    public static string? ExistingDirectory
    {
        get
        {
            using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                var location = key?.GetValue("InstallLocation") as string;
                return !string.IsNullOrEmpty(location) && File.Exists(Path.Combine(location, "PackWise.exe")) ? location : null;
            }
        }
    }

    public static string? ExistingVersion
    {
        get
        {
            using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                return key?.GetValue("DisplayVersion") as string;
            }
        }
    }

    public static bool HasPayload => typeof(Installer).Assembly.GetManifestResourceInfo("payload.zip") != null;

    /// <summary>PackWise processes running from the target folder (they lock files).</summary>
    public static Process[] RunningInstances(string directory) =>
        Process.GetProcessesByName("PackWise").Where(p => IsFrom(p, directory)).ToArray();

    public static void Install(InstallOptions options, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(options.Directory);
        var staging = directory + ".new";
        if (System.IO.Directory.Exists(staging))
        {
            System.IO.Directory.Delete(staging, true);
        }

        using (var payload = typeof(Installer).Assembly.GetManifestResourceStream("payload.zip"))
        using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            var total = archive.Entries.Sum(e => e.Length);
            long done = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    System.IO.Directory.CreateDirectory(target);
                    continue;
                }

                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));
                entry.ExtractToFile(target, overwrite: true);
                done += entry.Length;
                progress.Report(total == 0 ? 1 : 0.9 * done / total);
            }
        }

        // Swap in the new files only once everything unpacked, so a failed update leaves the old install working.
        if (System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.Delete(directory, true);
        }

        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(directory));
        System.IO.Directory.Move(staging, directory);

        var parts = new List<string> { "startmenu" };
        if (options.ContextMenu)
        {
            parts.Add("menu");
        }

        if (options.Associations)
        {
            parts.Add("associations");
        }

        if (options.DesktopShortcut)
        {
            parts.Add("desktop");
        }

        RunApp(directory, "--integrate " + string.Join(",", parts));
        progress.Report(1);
    }

    public static void Launch(string directory) =>
        Process.Start(new ProcessStartInfo(Path.Combine(directory, "PackWise.exe")) { UseShellExecute = true, WorkingDirectory = directory });

    private static void RunApp(string directory, string arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(directory, "PackWise.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
        };

        using (var process = Process.Start(info))
        {
            if (process != null && !process.WaitForExit(60000))
            {
                throw new InvalidOperationException("PackWise did not finish registering with Windows.");
            }

            if (process != null && process.ExitCode != 0)
            {
                throw new InvalidOperationException("PackWise could not register with Windows (exit code " + process.ExitCode + ").");
            }
        }
    }

    private static bool IsFrom(Process process, string directory)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return path != null && path.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
