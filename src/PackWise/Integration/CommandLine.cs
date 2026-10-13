using System.IO.Pipes;
using System.Text;

namespace PackWise.Integration;

/// <summary>A parsed command line, e.g. <c>--extract-here "C:\a.zip"</c> or a plain path to open.</summary>
public sealed record CommandRequest(string Verb, IReadOnlyList<string> Paths, IReadOnlySet<string> Options)
{
    public static readonly IReadOnlySet<string> BatchVerbs = new HashSet<string>(StringComparer.Ordinal)
    {
        "add", "zip", "7z", "extract-here", "extract-folder", "extract-to", "test", "convert-zip", "convert-7z", "checksum",
    };

    public bool IsBatch => BatchVerbs.Contains(Verb);

    public static CommandRequest Parse(IReadOnlyList<string> args)
    {
        var verb = "open";
        var paths = new List<string>();
        var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..].ToLowerInvariant();
                if (name == "integrate" && i + 1 < args.Count)
                {
                    verb = name;
                    foreach (var part in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        options.Add(part);
                    }
                }
                else if (name is "quiet" or "keep-settings")
                {
                    options.Add(name);
                }
                else
                {
                    verb = name;
                }
            }
            else if (arg.Length > 0)
            {
                paths.Add(Path.GetFullPath(arg.Trim('"')));
            }
        }

        return new CommandRequest(verb, paths, options);
    }
}

/// <summary>
/// Explorer starts one process per selected file. The first process for a verb collects the paths
/// sent by the others over a named pipe, so "Compress to ZIP" on ten files makes one archive.
/// </summary>
public static class SelectionCollector
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Returns the combined paths when this process should do the work, or null when the paths were
    /// handed to another PackWise process and this one should exit.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> CollectAsync(CommandRequest request)
    {
        var name = $"NorthstarLabs.PackWise.{request.Verb}.{Environment.UserName}";
        using var leader = new Mutex(initiallyOwned: true, name, out var isLeader);
        if (!isLeader)
        {
            return await ForwardAsync(name, request.Paths).ConfigureAwait(true) ? null : request.Paths;
        }

        var paths = new List<string>(request.Paths);
        var started = DateTime.UtcNow;
        try
        {
            while (DateTime.UtcNow - started < MaxWait)
            {
                using var server = new NamedPipeServerStream(name, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using var timeout = new CancellationTokenSource(QuietPeriod);
                try
                {
                    await server.WaitForConnectionAsync(timeout.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                using var reader = new StreamReader(server, Encoding.UTF8);
                var text = await reader.ReadToEndAsync().ConfigureAwait(true);
                paths.AddRange(text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }
        finally
        {
            leader.ReleaseMutex();
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<bool> ForwardAsync(string name, IReadOnlyList<string> paths)
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(250).ConfigureAwait(true);
                var bytes = Encoding.UTF8.GetBytes(string.Join('\n', paths));
                await client.WriteAsync(bytes).ConfigureAwait(true);
                return true;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }
        }

        return false;
    }
}
