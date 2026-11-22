using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolErp.Application.Common;

namespace SchoolErp.Infrastructure.Services;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Absolute path or a path relative to the API content root.</summary>
    public string RootPath { get; set; } = "App_Data/uploads";
    public string PublicBaseUrl { get; set; } = "/files";
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>
/// Local disk storage used for development and single-node deployments.
/// Production should switch to Azure Blob Storage with private containers and SAS URLs.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly StorageOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ICurrentUser _user;

    public LocalFileStorage(Microsoft.Extensions.Options.IOptions<StorageOptions> options, IHostEnvironment environment,
        ICurrentUser user)
    {
        _options = options.Value;
        _environment = environment;
        _user = user;
    }

    private string Root => Path.IsPathRooted(_options.RootPath)
        ? _options.RootPath
        : Path.Combine(_environment.ContentRootPath, _options.RootPath);

    public async Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType,
        CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Length > 16) extension = string.Empty;

        // Each school's uploads live in their own folder.
        var schoolFolder = _user.SchoolId is int schoolId ? $"school-{schoolId}" : "shared";
        var relativeDirectory = $"{schoolFolder}/{DateTime.UtcNow:yyyy/MM}";
        var directory = Path.Combine(Root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);

        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(directory, storedName);

        await using (var file = File.Create(fullPath))
        {
            await content.CopyToAsync(file, ct);
        }

        var relativePath = $"{relativeDirectory}/{storedName}".Replace('\\', '/');
        return new StoredFile(relativePath, GetDownloadUrl(relativePath, TimeSpan.FromDays(1)));
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        if (!TryResolveFullPath(Root, path, out var fullPath)) return Task.CompletedTask;
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public string? GetDownloadUrl(string path, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return path;

        // Local files are served by the gated /files/* endpoint. Signing the URL keeps a
        // plain <img src>/<a href> working after the Files:SignedFromUtc grace window.
        return FileUrlSigner.SignStoredUrl($"{_options.PublicBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
    }

    /// <summary>
    /// Resolves a stored relative path to a full path inside <paramref name="rootPath"/>.
    /// Rejects traversal segments, absolute paths, empty segments and anything that would
    /// escape the uploads root. Shared with the /files/* endpoint so both use one guard.
    /// </summary>
    public static bool TryResolveFullPath(string rootPath, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        var relative = relativePath.Replace('\\', '/');
        if (relative.Split('/').Any(s => s.Length == 0 || s is "." or ".." || s.Contains(':')))
            return false;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var candidate = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = candidate;
        return true;
    }
}
