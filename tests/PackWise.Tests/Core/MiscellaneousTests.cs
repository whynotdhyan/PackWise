using PackWise.Archiving;
using PackWise.Core.Archives;
using PackWise.Core.Hashing;
using PackWise.Core.Security;
using PackWise.Core.Settings;

namespace PackWise.Tests.Core;

public sealed class MiscellaneousTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Theory]
    [InlineData("setup.exe", FileRisk.Executable)]
    [InlineData("invoice.pdf.exe", FileRisk.Executable | FileRisk.DisguisedExtension)]
    [InlineData("run.ps1", FileRisk.Script)]
    [InlineData("budget.xlsm", FileRisk.MacroDocument)]
    [InlineData("shortcut.lnk", FileRisk.Shortcut)]
    [InlineData("photo.jpg", FileRisk.None)]
    [InlineData("report\u202Efdp.exe", FileRisk.Executable | FileRisk.SpoofedName)]
    public void Classifies_risky_file_types(string name, FileRisk expected)
    {
        Assert.Equal(expected, FileRiskClassifier.Classify(name));
    }

    [Fact]
    public void Checksums_match_known_vectors()
    {
        var path = _temp.Path("abc.txt");
        File.WriteAllText(path, "abc");

        var result = Checksums.Compute(path, ChecksumAlgorithms.Crc32 | ChecksumAlgorithms.Sha256);

        Assert.Equal("352441C2", result.Crc32);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", result.Sha256);
        Assert.Null(result.Sha512);
    }

    [Fact]
    public void Verifies_checksum_files_in_both_formats()
    {
        File.WriteAllText(_temp.Path("abc.txt"), "abc");
        File.WriteAllText(_temp.Path("list.sha256"),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad *abc.txt\n" +
            "SHA256 (missing.bin) = ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\n" +
            "0000000000000000000000000000000000000000000000000000000000000000  abc.txt\n");

        var results = Checksums.Verify(_temp.Path("list.sha256"));

        Assert.Equal([ChecksumMatch.Match, ChecksumMatch.Missing, ChecksumMatch.Mismatch], results.Select(r => r.Result));
    }

    [Fact]
    public void Verifies_sfv_files()
    {
        File.WriteAllText(_temp.Path("abc.txt"), "abc");
        File.WriteAllText(_temp.Path("list.sfv"), "; generated\nabc.txt 352441C2\n");

        var result = Assert.Single(Checksums.Verify(_temp.Path("list.sfv")));

        Assert.Equal(ChecksumMatch.Match, result.Result);
    }

    [Fact]
    public void Settings_survive_a_corrupted_file()
    {
        var path = _temp.Path("settings.json");
        File.WriteAllText(path, "{ not json");
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.NotNull(store.RecoveredFromBackup);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Settings_round_trip()
    {
        var store = new SettingsStore(_temp.Path("nested", "settings.json"));
        var settings = new AppSettings { Theme = ThemePreference.Dark, DefaultFormat = "7z" };
        settings.Remember(settings.RecentArchives, @"C:\a.zip");
        settings.Remember(settings.RecentArchives, @"C:\b.zip");
        settings.Remember(settings.RecentArchives, @"c:\A.zip");

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(ThemePreference.Dark, loaded.Theme);
        Assert.Equal([@"c:\A.zip", @"C:\b.zip"], loaded.RecentArchives);
    }

    [Fact]
    public void Archive_tree_synthesises_missing_folders()
    {
        var entries = new[]
        {
            new ArchiveEntry { Index = 0, Path = @"docs\guide\intro.md", IsDirectory = false, Size = 10 },
            new ArchiveEntry { Index = 1, Path = @"docs\readme.md", IsDirectory = false, Size = 5 },
            new ArchiveEntry { Index = 2, Path = "top.txt", IsDirectory = false, Size = 1 },
        };

        var root = ArchiveTree.Build(entries);

        var docs = root.Find("docs")!;
        Assert.True(docs.IsDirectory);
        Assert.Null(docs.Entry);
        Assert.Equal(15UL, docs.Size);
        Assert.Equal(2, docs.FileCount);
        Assert.Equal(@"docs\guide\intro.md", root.Find(@"docs\guide\intro.md")!.Path);
        Assert.Equal(3, root.DescendantEntries().Count());
    }

    public void Dispose() => _temp.Dispose();
}
