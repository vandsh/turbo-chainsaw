using Microsoft.Extensions.Configuration;
using TestProject.Services;

namespace TestProject.Tests;

public class PathSecurityServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly PathSecurityService _service;

    public PathSecurityServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "pathsec_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileBrowser:BrowseRoot"] = _tempRoot,
                ["FileBrowser:DefaultPageSize"] = "25"
            })
            .Build();

        _service = new PathSecurityService(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, true);
    }

    // --- ResolveSafePath ---
    [Fact]
    public void ResolveSafePath_NullOrEmpty_ReturnsRoot()
    {
        Assert.Equal(_tempRoot, _service.ResolveSafePath(null));
        Assert.Equal(_tempRoot, _service.ResolveSafePath(""));
        Assert.Equal(_tempRoot, _service.ResolveSafePath("  "));
    }

    [Fact]
    public void ResolveSafePath_ValidSubPath_ResolvesWithinRoot()
    {
        var result = _service.ResolveSafePath("subdir/file.txt");
        Assert.NotNull(result);
        Assert.StartsWith(_tempRoot, result);
        Assert.EndsWith("subdir/file.txt", result);
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("../../etc/shadow")]
    [InlineData("subdir/../../etc/passwd")]
    [InlineData("../")]
    public void ResolveSafePath_TraversalAttempt_ReturnsNull(string maliciousPath)
    {
        Assert.Null(_service.ResolveSafePath(maliciousPath));
    }

    [Fact]
    public void ResolveSafePath_NullByte_ReturnsNull()
    {
        Assert.Null(_service.ResolveSafePath("file\0.txt"));
        Assert.Null(_service.ResolveSafePath("sub\0dir/file.txt"));
    }

    [Fact]
    public void ResolveSafePath_DotDotInMiddle_StillInsideRoot_IsAllowed()
    {
        // "a/../b" resolves to just "b" under root — that's safe
        var result = _service.ResolveSafePath("a/../b");
        Assert.NotNull(result);
        Assert.StartsWith(_tempRoot, result);
    }

    // --- IsValidFileName ---

    [Theory]
    [InlineData("report.pdf", true)]
    [InlineData("my-file_v2.txt", true)]
    [InlineData("README", true)]
    [InlineData(".hidden", true)]
    [InlineData("file-name/with/txt", false)]
    public void IsValidFileName_ValidNames_ReturnsTrue(string name, bool expected)
    {
        Assert.Equal(expected, _service.IsValidFileName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValidFileName_EmptyOrWhitespace_ReturnsFalse(string? name)
    {
        Assert.False(_service.IsValidFileName(name!));
    }

    [Theory]
    [InlineData("dir/file.txt")]
    [InlineData("../passwd")]
    [InlineData("file\0.txt")]
    public void IsValidFileName_PathSeparatorsOrNullBytes_ReturnsFalse(string name)
    {
        Assert.False(_service.IsValidFileName(name));
    }

    [Fact]
    public void IsValidFileName_TooLong_ReturnsFalse()
    {
        var longName = new string('a', 256);
        Assert.False(_service.IsValidFileName(longName));
    }

    [Fact]
    public void IsValidFileName_MaxLength_ReturnsTrue()
    {
        var name = new string('a', 255);
        Assert.True(_service.IsValidFileName(name));
    }

    // --- Config ---

    [Fact]
    public void RootPath_MatchesConfigured()
    {
        Assert.Equal(_tempRoot, _service.RootPath);
    }

    [Fact]
    public void DefaultPageSize_ReadsFromConfig()
    {
        Assert.Equal(25, _service.DefaultPageSize);
    }
}
