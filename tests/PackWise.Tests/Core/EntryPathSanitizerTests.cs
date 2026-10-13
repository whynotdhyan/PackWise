using PackWise.Core.Extraction;

namespace PackWise.Tests.Core;

public sealed class EntryPathSanitizerTests
{
    [Theory]
    [InlineData("docs/readme.txt", @"docs\readme.txt")]
    [InlineData(@"a\.\b\c.txt", @"a\b\c.txt")]
    [InlineData("folder//file.txt", @"folder\file.txt")]
    public void Keeps_ordinary_paths(string raw, string expected)
    {
        var result = EntryPathSanitizer.Sanitize(raw);

        Assert.Equal(expected, result.RelativePath);
        Assert.Equal(PathIssues.None, result.Issues);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData(@"..\..\Windows\System32\evil.dll")]
    [InlineData("safe/../../escape.txt")]
    [InlineData("a/ .. /b.txt")]
    public void Rejects_parent_traversal(string raw)
    {
        var result = EntryPathSanitizer.Sanitize(raw);

        Assert.True(result.IsRejected);
        Assert.True(result.Issues.HasFlag(PathIssues.ParentTraversal));
    }

    [Theory]
    [InlineData(@"C:\Windows\evil.dll", @"Windows\evil.dll", PathIssues.DriveOrNetworkPrefix)]
    [InlineData(@"\\server\share\evil.txt", @"server\share\evil.txt", PathIssues.DriveOrNetworkPrefix)]
    [InlineData(@"\\?\C:\evil.txt", @"evil.txt", PathIssues.DriveOrNetworkPrefix)]
    [InlineData("/etc/passwd", @"etc\passwd", PathIssues.AbsolutePath)]
    public void Strips_absolute_and_device_prefixes(string raw, string expected, PathIssues issue)
    {
        var result = EntryPathSanitizer.Sanitize(raw);

        Assert.Equal(expected, result.RelativePath);
        Assert.True(result.Issues.HasFlag(issue));
    }

    [Fact]
    public void Neutralises_alternate_data_streams()
    {
        var result = EntryPathSanitizer.Sanitize("decoy.txt:payload.exe");

        Assert.Equal("decoy.txt_payload.exe", result.RelativePath);
        Assert.True(result.Issues.HasFlag(PathIssues.AlternateDataStream));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("COM1.log", "_COM1.log")]
    [InlineData("lpt¹", "_lpt¹")]
    [InlineData("console.txt", "console.txt")]
    public void Prefixes_reserved_device_names(string raw, string expected)
    {
        Assert.Equal(expected, EntryPathSanitizer.Sanitize(raw).RelativePath);
    }

    [Fact]
    public void Trims_trailing_dots_and_spaces_that_Windows_would_drop()
    {
        var result = EntryPathSanitizer.Sanitize("report.pdf. . ");

        Assert.Equal("report.pdf", result.RelativePath);
        Assert.True(result.Issues.HasFlag(PathIssues.TrailingDotOrSpace));
    }

    [Fact]
    public void Removes_direction_override_characters()
    {
        var result = EntryPathSanitizer.Sanitize("invoice\u202Efdp.exe");

        Assert.Equal("invoicefdp.exe", result.RelativePath);
        Assert.True(result.Issues.HasFlag(PathIssues.DirectionOverride));
    }

    [Fact]
    public void Replaces_invalid_and_control_characters()
    {
        var result = EntryPathSanitizer.Sanitize("what?<is>\u0001this|.txt");

        Assert.Equal("what__is__this_.txt", result.RelativePath);
    }

    [Fact]
    public void Shortens_overlong_names_but_keeps_extension()
    {
        var result = EntryPathSanitizer.Sanitize(new string('a', 300) + ".txt");

        Assert.Equal(EntryPathSanitizer.MaxComponentLength, result.RelativePath!.Length);
        Assert.EndsWith(".txt", result.RelativePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("./.")]
    public void Rejects_empty_paths(string raw)
    {
        Assert.True(EntryPathSanitizer.Sanitize(raw).IsRejected);
    }
}
