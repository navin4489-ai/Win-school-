using SchoolErp.Application.Common;
using Xunit;

namespace SchoolErp.Tests;

public sealed class AppDownloadTests
{
    [Fact]
    public void ResolvePath_UsesConfiguredAbsolutePath()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "wintech", "app.apk"));
        Assert.Equal(expected, AppDownloadOptions.ResolvePath("C:\\content-root", expected));
    }

    [Fact]
    public void ResolvePath_ResolvesRelativePathAgainstContentRoot()
    {
        var expected = Path.GetFullPath(Path.Combine("C:\\content-root", "App_Data", "app", "WintechSchool.apk"));
        Assert.Equal(
            expected,
            AppDownloadOptions.ResolvePath("C:\\content-root", "App_Data/app/WintechSchool.apk"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvePath_RejectsBlankConfiguration(string? configuredPath)
    {
        Assert.Throws<ArgumentException>(() => AppDownloadOptions.ResolvePath("C:\\content-root", configuredPath!));
    }
}
