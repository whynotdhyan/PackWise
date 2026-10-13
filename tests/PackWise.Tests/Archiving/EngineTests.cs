using System.IO.Compression;
using PackWise.Archiving;

namespace PackWise.Tests.Archiving;

public sealed class EngineTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public void Engine_reports_trusted_and_creatable_formats()
    {
        var formats = ArchiveReader.Formats;

        Assert.Contains(formats, f => f.Name == "7z" && f.IsTrusted && f.CanCreate && f.CanEncryptNames);
        Assert.Contains(formats, f => f.Name == "zip" && f.IsTrusted && f.CanCreate);
        Assert.Contains(formats, f => f.Name == "Rar5" && f.IsTrusted && !f.CanCreate);
        Assert.Contains(formats, f => f.Name == "NTFS" && !f.IsTrusted);
        Assert.Equal(["zip", "7z"], ArchiveWriter.CreatableFormats.Select(f => f.Name));
    }

    [Theory]
    [InlineData("zip", null)]
    [InlineData("7z", null)]
    [InlineData("zip", "s3cret!")]
    [InlineData("7z", "s3cret!")]
    public async Task Create_list_test_and_extract_round_trip(string formatName, string? password)
    {
        var source = _temp.CreateTree(new()
        {
            ["photos/beach.txt"] = "sand and sea",
            ["photos/nested/deep.txt"] = new string('x', 100_000),
            ["readme.md"] = "# Hello ünïcødé",
        });

        var archivePath = _temp.Path($"out.{formatName}");
        await Task.Run(() =>
        {
            var items = ArchiveSourceItem.Collect([Path.Combine(source, "photos"), Path.Combine(source, "readme.md")]);
            using var pending = ArchiveWriter.Create(archivePath, items, new CreateArchiveOptions
            {
                Format = ArchiveWriter.FindFormat(formatName),
                Password = password,
                EncryptFileNames = formatName == "7z" && password is not null,
            });
            pending.Commit();
        }, TestContext.Current.CancellationToken);

        await Task.Run(() =>
        {
            using var reader = ArchiveReader.Open(archivePath, new ArchiveOpenOptions { Password = password });
            Assert.Equal(formatName, reader.Info.Format.Name);
            Assert.Contains(reader.Entries, e => e.Path == @"photos\nested\deep.txt" && e.Size == 100_000);
            Assert.Contains(reader.Entries, e => e.Path == "readme.md" && !e.IsDirectory);
            Assert.Contains(reader.Entries, e => e.Path == "photos" && e.IsDirectory);
            if (password is not null)
            {
                Assert.All(reader.Entries.Where(e => !e.IsDirectory), e => Assert.True(e.IsEncrypted));
            }

            var test = reader.Test();
            Assert.True(test.Succeeded);

            var target = new MemoryTarget();
            var run = reader.Extract(null, target);
            Assert.True(run.Succeeded);
            Assert.Equal("# Hello ünïcødé", target.Text("readme.md"));
            Assert.Equal(100_000, target.Text(@"photos\nested\deep.txt").Length);
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Encrypted_names_require_password_to_open()
    {
        var source = _temp.CreateTree(new() { ["secret.txt"] = "hidden" });
        var archivePath = _temp.Path("names.7z");

        await Task.Run(() =>
        {
            using var pending = ArchiveWriter.Create(archivePath, ArchiveSourceItem.Collect([Path.Combine(source, "secret.txt")]), new CreateArchiveOptions
            {
                Format = ArchiveWriter.FindFormat("7z"),
                Password = "pw",
                EncryptFileNames = true,
            });
            pending.Commit();
        }, TestContext.Current.CancellationToken);

        await Task.Run(() =>
        {
            Assert.Throws<ArchivePasswordException>(() => ArchiveReader.Open(archivePath));

            var provider = new SequencePasswordProvider("wrong", "pw");
            using var reader = ArchiveReader.Open(archivePath, new ArchiveOpenOptions { PasswordProvider = provider });
            Assert.True(reader.Info.HasEncryptedHeaders);
            Assert.Equal(2, provider.Calls);
            Assert.Single(reader.Entries);
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Wrong_password_is_reported_per_entry()
    {
        var source = _temp.CreateTree(new() { ["a.txt"] = "alpha" });
        var archivePath = _temp.Path("pw.zip");

        await Task.Run(() =>
        {
            using var pending = ArchiveWriter.Create(archivePath, ArchiveSourceItem.Collect([Path.Combine(source, "a.txt")]), new CreateArchiveOptions
            {
                Format = ArchiveWriter.FindFormat("zip"),
                Password = "right",
            });
            pending.Commit();

            using var reader = ArchiveReader.Open(archivePath, new ArchiveOpenOptions { Password = "wrong" });
            var run = reader.Test();
            Assert.False(run.Succeeded);
            Assert.Contains(run.Outcomes, o => o.Result is EntryResult.WrongPassword or EntryResult.DataError or EntryResult.CrcError);
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reads_zip_written_by_other_tools_and_flags_traversal_names()
    {
        var archivePath = _temp.Path("crafted.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "../../evil.txt", "escape");
            WriteEntry(zip, "ok/inner.txt", "fine");
        }

        await Task.Run(() =>
        {
            using var reader = ArchiveReader.Open(archivePath);
            Assert.Equal(2, reader.Entries.Count);
            Assert.Contains(reader.Entries, e => e.Path.Contains("evil.txt", StringComparison.Ordinal));
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Rewrite_removes_and_adds_entries()
    {
        var source = _temp.CreateTree(new() { ["one.txt"] = "1", ["two.txt"] = "2", ["three.txt"] = "3" });
        var archivePath = _temp.Path("edit.7z");

        await Task.Run(() =>
        {
            using (var pending = ArchiveWriter.Create(archivePath, ArchiveSourceItem.Collect([Path.Combine(source, "one.txt"), Path.Combine(source, "two.txt")]), new CreateArchiveOptions { Format = ArchiveWriter.FindFormat("7z") }))
            {
                pending.Commit();
            }

            PendingArchive rewritten;
            using (var reader = ArchiveReader.Open(archivePath))
            {
                var one = reader.Entries.Single(e => e.Path == "one.txt").Index;
                rewritten = ArchiveWriter.Rewrite(reader, [one], ArchiveSourceItem.Collect([Path.Combine(source, "three.txt")]));
            }

            using (rewritten)
            {
                rewritten.Commit();
            }

            using var result = ArchiveReader.Open(archivePath);
            Assert.Equal(["three.txt", "two.txt"], result.Entries.Select(e => e.Path).Order());
            Assert.True(result.Test().Succeeded);
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Non_archives_are_rejected()
    {
        var textFile = _temp.CreateTree(new() { ["plain.txt"] = "just text" });

        await Task.Run(() =>
        {
            Assert.Throws<ArchiveNotRecognizedException>(() => ArchiveReader.Open(Path.Combine(textFile, "plain.txt")));
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Engine_calls_from_UI_thread_are_rejected()
    {
        Exception? caught = null;
        var thread = new Thread(() => caught = Record.Exception(() => ArchiveReader.Open(_temp.Path("missing.zip"))));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(caught);
    }

    public void Dispose() => _temp.Dispose();

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }
}
