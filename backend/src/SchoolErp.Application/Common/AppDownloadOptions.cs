namespace SchoolErp.Application.Common;

/// <summary>
/// Location of the Android APK offered on the login page. The file deliberately lives
/// outside the repository so a new build can be published without a web redeploy.
/// </summary>
public sealed class AppDownloadOptions
{
    public const string SectionName = "AppDownload";

    /// <summary>Absolute path, or a path relative to the API content root.</summary>
    public string ApkPath { get; set; } = "App_Data/app/WintechSchool.apk";

    /// <summary>File name sent to the browser in Content-Disposition.</summary>
    public string FileName { get; set; } = "WintechSchool.apk";

    /// <summary>Largest APK the super admin may publish through the upload endpoint.</summary>
    public long MaxUploadBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>Resolves a configured path against the API content root.</summary>
    public static string ResolvePath(string contentRoot, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new ArgumentException("An APK path must be configured.", nameof(configuredPath));

        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(contentRoot, configuredPath));
    }
}
