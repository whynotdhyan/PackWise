using PackWise.Core.Duplicates;

namespace PackWise.Tests.Core;

public sealed class DuplicateScannerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public async Task Groups_identical_content_regardless_of_name()
    {
        var root = _temp.CreateTree(new()
        {
            ["a/photo.jpg"] = "same bytes",
            ["b/copy of photo.jpg"] = "same bytes",
            ["c/different-name.bin"] = "same bytes",
            ["a/unique.txt"] = "something else",
            ["b/same-size.txt"] = "same bytez",
        });

        var result = await DuplicateScanner.ScanAsync(new DuplicateScanOptions { Roots = [root] }, cancellationToken: TestContext.Current.CancellationToken);

        var group = Assert.Single(result.Groups);
        Assert.Equal(3, group.Files.Count);
        Assert.Equal(2 * "same bytes".Length, group.ReclaimableBytes);
        Assert.Equal(64, group.Sha256.Length);
    }

    [Fact]
    public async Task Same_name_with_different_content_is_not_a_duplicate()
    {
        var root = _temp.CreateTree(new() { ["x/report.pdf"] = "version one", ["y/report.pdf"] = "version two" });

        var result = await DuplicateScanner.ScanAsync(new DuplicateScanOptions { Roots = [root] }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Large_files_differing_only_in_the_middle_are_told_apart()
    {
        var root = _temp.CreateTree([]);
        var size = 300_000;
        var a = new byte[size];
        var b = new byte[size];
        b[size / 3] = 1;
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "a.bin"), a);
        File.WriteAllBytes(Path.Combine(root, "b.bin"), b);

        var result = await DuplicateScanner.ScanAsync(new DuplicateScanOptions { Roots = [root] }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Hard_links_are_not_counted_as_reclaimable()
    {
        var root = _temp.CreateTree(new() { ["original.txt"] = "linked content" });
        var link = Path.Combine(root, "hardlink.txt");
        if (!CreateHardLink(link, Path.Combine(root, "original.txt")))
        {
            Assert.Skip("Hard links are not supported on this volume.");
        }

        var result = await DuplicateScanner.ScanAsync(new DuplicateScanOptions { Roots = [root] }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Byte_verification_confirms_groups()
    {
        var root = _temp.CreateTree(new() { ["1.txt"] = "twin", ["2.txt"] = "twin" });

        var result = await DuplicateScanner.ScanAsync(new DuplicateScanOptions { Roots = [root], VerifyByteByByte = true }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DuplicateVerification.ByteForByte, Assert.Single(result.Groups).Verification);
    }

    [Fact]
    public async Task Keep_rules_always_leave_one_copy()
    {
        var root = _temp.CreateTree(new() { ["old/a.txt"] = "dup", ["new/a.txt"] = "dup" });
        File.SetLastWriteTimeUtc(Path.Combine(root, "old", "a.txt"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var result = await DuplicateScanner.ScanAsync(new DuplicateScanOptions { Roots = [root] }, cancellationToken: TestContext.Current.CancellationToken);

        var newest = KeepRules.SelectRemovals(result.Groups, KeepRule.Newest);
        var oldest = KeepRules.SelectRemovals(result.Groups, KeepRule.Oldest);

        Assert.Equal([Path.Combine(root, "old", "a.txt")], newest);
        Assert.Equal([Path.Combine(root, "new", "a.txt")], oldest);
    }

    [Fact]
    public async Task Moving_to_review_folder_refuses_to_remove_every_copy()
    {
        var root = _temp.CreateTree(new() { ["a.txt"] = "dup", ["b.txt"] = "dup" });
        var result = await DuplicateScanner.ScanAsync(new DuplicateScanOptions { Roots = [root] }, cancellationToken: TestContext.Current.CancellationToken);
        var group = Assert.Single(result.Groups);

        var review = _temp.Path("review");
        var all = DuplicateActions.MoveToReviewFolder(group.Files, result.Groups, review);
        Assert.Equal(0, all.Succeeded);

        var one = DuplicateActions.MoveToReviewFolder([group.Files[0]], result.Groups, review);
        Assert.Equal(1, one.Succeeded);
        Assert.Single(Directory.EnumerateFiles(review, "*", SearchOption.AllDirectories));
    }

    public void Dispose() => _temp.Dispose();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    private static bool CreateHardLink(string link, string existing) => CreateHardLink(link, existing, IntPtr.Zero);
}
