using System.IO.Compression;
using PackWise.Archiving;
using PackWise.Core.Extraction;
using PackWise.Core.Security;

namespace PackWise.Tests.Core;

public sealed class SafeExtractionTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public async Task Crafted_paths_never_escape_the_destination()
    {
        var archive = CreateZip("crafted.zip", new()
        {
            ["../../outside.txt"] = "escape",
            ["C:/Windows/evil.txt"] = "drive",
            ["decoy.txt:hidden.exe"] = "stream",
            ["CON"] = "device",
            ["ok/inner.txt"] = "fine",
        });
        var destination = _temp.Path("out");

        var report = await ExtractAsync(archive, destination);

        Assert.Equal(4, report.Written);
        Assert.Equal(1, report.Blocked);
        Assert.False(File.Exists(_temp.Path("outside.txt")));
        Assert.True(File.Exists(Path.Combine(destination, "ok", "inner.txt")));
        Assert.True(File.Exists(Path.Combine(destination, "Windows", "evil.txt")));
        Assert.True(File.Exists(Path.Combine(destination, "decoy.txt_hidden.exe")));
        Assert.False(File.Exists(Path.Combine(destination, "decoy.txt") + ":hidden.exe"));
        Assert.True(File.Exists(Path.Combine(destination, "_CON")));
        Assert.All(Directory.EnumerateFiles(_temp.Root, "*", SearchOption.AllDirectories), f =>
            Assert.True(f.StartsWith(destination, StringComparison.OrdinalIgnoreCase) || f.EndsWith("crafted.zip", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Existing_files_are_never_overwritten_without_a_decision()
    {
        var archive = CreateZip("a.zip", new() { ["note.txt"] = "from archive" });
        var destination = _temp.Path("out");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "note.txt"), "original");

        var report = await ExtractAsync(archive, destination, ConflictPolicy.Ask, resolver: null);

        Assert.Equal("original", File.ReadAllText(Path.Combine(destination, "note.txt")));
        Assert.Equal("from archive", File.ReadAllText(Path.Combine(destination, "note (2).txt")));
        Assert.Equal(1, report.Written);
    }

    [Theory]
    [InlineData(ConflictChoice.Skip, "original", false)]
    [InlineData(ConflictChoice.Replace, "from archive", false)]
    [InlineData(ConflictChoice.KeepBoth, "original", true)]
    public async Task Conflict_choices_are_honoured(ConflictChoice choice, string expectedContent, bool expectCopy)
    {
        var archive = CreateZip("a.zip", new() { ["note.txt"] = "from archive" });
        var destination = _temp.Path("out");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "note.txt"), "original");

        var resolver = new FixedResolver(choice);
        await ExtractAsync(archive, destination, ConflictPolicy.Ask, resolver, recycle: false);

        Assert.Equal(1, resolver.Calls);
        Assert.Equal(expectedContent, File.ReadAllText(Path.Combine(destination, "note.txt")));
        Assert.Equal(expectCopy, File.Exists(Path.Combine(destination, "note (2).txt")));
    }

    [Fact]
    public async Task Cancel_during_planning_writes_nothing()
    {
        var archive = CreateZip("a.zip", new() { ["a.txt"] = "1", ["note.txt"] = "2" });
        var destination = _temp.Path("out");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "note.txt"), "original");

        await Task.Run(() =>
        {
            using var reader = ArchiveReader.Open(archive);
            var plan = ExtractionPlanner.Create(reader.Entries, new ExtractionRequest { Destination = destination }, new FixedResolver(ConflictChoice.Cancel));
            Assert.True(plan.Canceled);
        });

        Assert.Single(Directory.EnumerateFiles(destination));
    }

    [Fact]
    public async Task Mark_of_the_web_is_copied_to_extracted_files()
    {
        var archive = CreateZip("download.zip", new() { ["run.exe"] = "MZ", ["readme.txt"] = "hi" });
        MarkOfTheWeb.Write(archive, MarkOfTheWeb.CreateInternetZone("https://example.com/download.zip"));
        var destination = _temp.Path("out");

        await ExtractAsync(archive, destination, zone: MarkOfTheWeb.Read(archive));

        Assert.NotNull(MarkOfTheWeb.Read(Path.Combine(destination, "run.exe")));
        Assert.NotNull(MarkOfTheWeb.Read(Path.Combine(destination, "readme.txt")));
    }

    [Fact]
    public async Task Extracting_a_subfolder_is_relative_to_the_browsed_folder()
    {
        var archive = CreateZip("nested.zip", new() { ["a/b/c.txt"] = "deep", ["a/top.txt"] = "top" });
        var destination = _temp.Path("out");

        await Task.Run(() =>
        {
            using var reader = ArchiveReader.Open(archive);
            var selected = reader.Entries.Where(e => e.Path.StartsWith(@"a\b", StringComparison.Ordinal)).Select(e => e.Index).ToHashSet();
            var plan = ExtractionPlanner.Create(reader.Entries, new ExtractionRequest { Destination = destination, SelectedIndices = selected, BaseFolder = "a" });
            SafeExtraction.Execute(reader, plan, new ExtractionOptions());
        });

        Assert.True(File.Exists(Path.Combine(destination, "b", "c.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "top.txt")));
    }

    [Fact]
    public async Task Byte_budget_stops_runaway_output()
    {
        var archive = CreateZip("bomb.zip", new() { ["zeros.bin"] = new string('\0', 5_000_000) });
        var destination = _temp.Path("out");

        var report = await Task.Run(() =>
        {
            using var reader = ArchiveReader.Open(archive);
            var plan = ExtractionPlanner.Create(reader.Entries, new ExtractionRequest { Destination = destination });
            return SafeExtraction.Execute(reader, plan, new ExtractionOptions { ByteBudget = 1_000_000 });
        });

        Assert.True(report.LimitExceeded);
        Assert.Equal(0, report.Written);
        Assert.Empty(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Duplicate_names_inside_an_archive_do_not_overwrite_each_other()
    {
        var archive = _temp.Path("dupes.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            Write(zip, "same.txt", "first");
            Write(zip, "SAME.txt", "second");
        }

        var destination = _temp.Path("out");
        await ExtractAsync(archive, destination, ConflictPolicy.KeepBoth);

        var contents = Directory.EnumerateFiles(destination).Select(File.ReadAllText).Order().ToList();
        Assert.Equal(["first", "second"], contents);
    }

    public void Dispose() => _temp.Dispose();

    private static Task<ExtractionReport> ExtractAsync(
        string archive,
        string destination,
        ConflictPolicy policy = ConflictPolicy.Ask,
        IConflictResolver? resolver = null,
        string? zone = null,
        bool recycle = true) =>
        Task.Run(() =>
        {
            using var reader = ArchiveReader.Open(archive);
            var plan = ExtractionPlanner.Create(reader.Entries, new ExtractionRequest { Destination = destination, ConflictPolicy = policy }, resolver);
            return SafeExtraction.Execute(reader, plan, new ExtractionOptions { ZoneIdentifier = zone, RecycleReplacedFiles = recycle });
        });

    private string CreateZip(string name, Dictionary<string, string> entries)
    {
        var path = _temp.Path(name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            Write(zip, entryName, content);
        }

        return path;
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    private sealed class FixedResolver(ConflictChoice choice) : IConflictResolver
    {
        public int Calls { get; private set; }

        public ConflictResolution Resolve(ConflictInfo conflict)
        {
            Calls++;
            return new ConflictResolution(choice, false);
        }
    }
}
