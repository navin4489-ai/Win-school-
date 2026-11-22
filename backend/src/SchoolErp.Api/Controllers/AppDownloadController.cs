using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SchoolErp.Application.Common;

namespace SchoolErp.Api.Controllers;

/// <summary>
/// Public download of the latest Android APK, plus a super-admin endpoint to publish a
/// replacement. The APK is read from a configured path outside the repository, so it can
/// be replaced without redeploying either the API or the web portal.
/// </summary>
[ApiController]
[Route("api/app")]
public sealed class AppDownloadController : ControllerBase
{
    public const string ApkContentType = "application/vnd.android.package-archive";

    private readonly AppDownloadOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ICurrentUser _user;

    public AppDownloadController(
        IOptions<AppDownloadOptions> options,
        IWebHostEnvironment environment,
        ICurrentUser user)
    {
        _options = options.Value;
        _environment = environment;
        _user = user;
    }

    /// <summary>Downloads the published APK. Anonymous by design: the login page links to it.</summary>
    [HttpGet("download")]
    [HttpHead("download")]
    [AllowAnonymous]
    public IActionResult Download()
    {
        var path = AppDownloadOptions.ResolvePath(_environment.ContentRootPath, _options.ApkPath);
        if (!System.IO.File.Exists(path))
        {
            return NotFound(new
            {
                detail = "No Android app build has been published yet. Configure AppDownload:ApkPath or upload one."
            });
        }

        var file = PhysicalFile(path, ApkContentType, _options.FileName);
        file.EnableRangeProcessing = true;
        Response.Headers.CacheControl = "public, max-age=300";
        return file;
    }

    /// <summary>Replaces the published APK. Only the platform super admin may do this.</summary>
    [HttpPost("download")]
    [Authorize]
    [RequestSizeLimit(210_000_000)]
    public async Task<IActionResult> Publish([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (!_user.IsPlatformAdmin) return Forbid();
        if (file is null || file.Length == 0)
            return BadRequest(new { detail = "An APK file is required." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".apk", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { detail = "Only .apk files are accepted." });
        if (file.Length > _options.MaxUploadBytes)
            return BadRequest(new { detail = $"APK exceeds the {_options.MaxUploadBytes} byte limit." });

        var path = AppDownloadOptions.ResolvePath(_environment.ContentRootPath, _options.ApkPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporary = path + ".upload-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await file.CopyToAsync(stream, ct);
            }

            if (System.IO.File.Exists(path))
            {
                System.IO.File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                System.IO.File.Move(temporary, path);
            }

            return Ok(new
            {
                fileName = _options.FileName,
                sizeBytes = file.Length,
                publishedAtUtc = DateTime.UtcNow
            });
        }
        finally
        {
            if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary);
        }
    }
}
