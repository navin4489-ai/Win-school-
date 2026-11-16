using SchoolErp.Application.Common;
using Xunit;

namespace SchoolErp.Tests;

/// <summary>
/// Unit tests for <see cref="FileUrlSigner"/>: signing, validation, idempotency,
/// tamper rejection and a fixed HMAC vector that the Node migration tool must reproduce.
/// </summary>
public sealed class FileUrlSignerTests
{
    private const string Secret = "test-secret-key";
    private const string SamplePath = "/files/db/00000000000000000000000000000001.jpg";

    public FileUrlSignerTests()
    {
        // The signer holds a process-wide secret; every test uses the same one so the
        // fixed vector below stays stable regardless of test ordering.
        FileUrlSigner.Initialize(Secret);
    }

    [Fact]
    public void Sign_Then_Validate_Succeeds()
    {
        var signed = FileUrlSigner.SignStoredUrl(SamplePath);
        Assert.NotNull(signed);
        Assert.Contains("?sig=", signed);

        var sig = signed!.Split("?sig=")[1];
        Assert.True(FileUrlSigner.Validate(SamplePath, sig));
    }

    [Fact]
    public void Validate_TamperedSignature_Fails()
    {
        var signed = FileUrlSigner.SignStoredUrl(SamplePath)!;
        var sig = signed.Split("?sig=")[1];
        var tampered = sig[..^1] + (sig[^1] == '0' ? '1' : '0');
        Assert.False(FileUrlSigner.Validate(SamplePath, tampered));
    }

    [Fact]
    public void Validate_WrongPath_Fails()
    {
        var sig = FileUrlSigner.SignStoredUrl(SamplePath)!.Split("?sig=")[1];
        var otherPath = "/files/db/00000000000000000000000000000002.jpg";
        Assert.False(FileUrlSigner.Validate(otherPath, sig));
    }

    [Fact]
    public void SignStoredUrl_IsIdempotent_NeverDoubleSigns()
    {
        var once = FileUrlSigner.SignStoredUrl(SamplePath);
        var twice = FileUrlSigner.SignStoredUrl(once);
        Assert.Equal(once, twice);
        Assert.Single(twice!.Split("?sig=")[1..]); // exactly one sig segment
    }

    [Fact]
    public void SignStoredUrl_StoredForm_And_AbsoluteForm_ShareSignature()
    {
        var stored = FileUrlSigner.SignStoredUrl("db/00000000000000000000000000000001.jpg")!;
        var absolute = FileUrlSigner.SignStoredUrl(SamplePath)!;
        Assert.Equal(stored.Split("?sig=")[1], absolute.Split("?sig=")[1]);
    }

    [Fact]
    public void SignStoredUrl_NonDbPath_LeftUnchanged()
    {
        Assert.Equal("/files/local/x.png", FileUrlSigner.SignStoredUrl("/files/local/x.png"));
        Assert.Equal("https://cdn.example.com/a.png", FileUrlSigner.SignStoredUrl("https://cdn.example.com/a.png"));
        Assert.Null(FileUrlSigner.SignStoredUrl(null));
    }

    [Fact]
    public void Compute_MatchesKnownVector()
    {
        // Must equal the value produced by the Node tool:
        //   crypto.createHmac('sha256','test-secret-key')
        //     .update('/files/db/00000000000000000000000000000001.jpg').digest('hex').slice(0,32)
        Assert.Equal("926dbaff777cb3f0900745274ba1e68d", FileUrlSigner.Compute(SamplePath));
    }

    [Fact]
    public void Validate_RejectsMissingSignature() =>
        Assert.False(FileUrlSigner.Validate(SamplePath, null));
}
