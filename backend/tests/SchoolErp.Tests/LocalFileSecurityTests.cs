using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SchoolErp.Application.Common;
using SchoolErp.Infrastructure.Services;
using Xunit;

namespace SchoolErp.Tests;

/// <summary>
/// Regression tests for the local-disk /files/* route: the signer must cover local upload
/// paths, the shared access gate must deny unsigned anonymous requests, and stored paths
/// must never resolve outside the uploads root.
/// </summary>
public sealed class LocalFileSecurityTests
{
    private const string LocalPath = "/files/school-2/2026/11/9cb2febc866e422593e09458ba138f35.png";
    private const string StoredPath = "school-2/2026/11/9cb2febc866e422593e09458ba138f35.png";

    [Fact]
    public void LocalFileStorage_GetDownloadUrl_SignsLocalPath()
    {
        var storage = new LocalFileStorage(
            Options.Create(new StorageOptions { RootPath = "uploads", PublicBaseUrl = "/files" }),
            new TestHostEnvironment(Path.GetTempPath()),
            new TestCurrentUser { SchoolId = 2 });

        var url = storage.GetDownloadUrl(StoredPath, TimeSpan.FromDays(1));

        Assert.NotNull(url);
        Assert.StartsWith(LocalPath + "?sig=", url);
        var sig = url!.Split("?sig=")[1];
        Assert.True(FileUrlSigner.Validate(LocalPath, sig));
        Assert.Equal(url, FileUrlSigner.SignStoredUrl(url)); // idempotent
    }

    [Fact]
    public void LocalPath_Signature_RoundTripRejectsTamperingAndOtherPaths()
    {
        var signed = FileUrlSigner.SignStoredUrl(LocalPath)!;
        var sig = signed.Split("?sig=")[1];
        var tampered = sig[..^1] + (sig[^1] == '0' ? '1' : '0');

        Assert.True(FileUrlSigner.Validate(LocalPath, sig));
        Assert.False(FileUrlSigner.Validate(LocalPath, tampered));
        Assert.False(FileUrlSigner.Validate("/files/school-3/2026/11/9cb2febc866e422593e09458ba138f35.png", sig));
        Assert.False(FileUrlSigner.Validate("/files/school-2/../secret.png", sig));
    }

    [Fact]
    public void AccessPolicy_AllowsSignatureOrBearerOrGrace_ButNotAnonymous()
    {
        var sig = FileUrlSigner.SignStoredUrl(LocalPath)!.Split("?sig=")[1];
        var now = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(FileAccessPolicy.IsAllowed(LocalPath, sig, false, null, now));
        Assert.True(FileAccessPolicy.IsAllowed(LocalPath, null, true, null, now));
        Assert.True(FileAccessPolicy.IsAllowed(LocalPath, null, false, now.AddMinutes(1), now));
        Assert.False(FileAccessPolicy.IsAllowed(LocalPath, null, false, null, now));
        Assert.False(FileAccessPolicy.IsAllowed(LocalPath, null, false, now.AddMinutes(-1), now));
        var tampered = sig[..^1] + (sig[^1] == '0' ? '1' : '0');
        Assert.False(FileAccessPolicy.IsAllowed(LocalPath, tampered, false, null, now));
    }

    [Fact]
    public void TryResolveFullPath_KeepsLegitimateUploadInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "schoolerp-uploads");
        Assert.True(LocalFileStorage.TryResolveFullPath(root, StoredPath, out var fullPath));
        Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, fullPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("9cb2febc866e422593e09458ba138f35.png", fullPath);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("school-2/../../secret.txt")]
    [InlineData("school-2\\..\\secret.txt")]
    [InlineData("school-2/./x.png")]
    [InlineData("/etc/passwd")]
    public void TryResolveFullPath_RejectsTraversal(string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "schoolerp-uploads");
        Assert.False(LocalFileStorage.TryResolveFullPath(root, path, out _));
    }
}

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public TestHostEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;

    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "SchoolErp.Tests";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
