using SchoolErp.Application.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Infrastructure.Persistence;

namespace SchoolErp.Infrastructure.Services;

/// <summary>
/// Stores uploads in the database so they survive redeploys on hosts without a persistent
/// disk (e.g. Render's free plan). Files are served by the API at /files/db/{id}.
/// </summary>
public sealed class DatabaseFileStorage : IFileStorage
{
    public const string RoutePrefix = "/files/db/";

    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;

    public DatabaseFileStorage(AppDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType,
        CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (extension.Length > 10) extension = string.Empty;

        var blob = new StoredBlob
        {
            SchoolId = _user.SchoolId,
            FileName = Path.GetFileName(fileName ?? "upload"),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Content = buffer.ToArray(),
            SizeBytes = buffer.Length
        };
        _db.StoredBlobs.Add(blob);
        await _db.SaveChangesAsync(ct);

        var path = $"db/{blob.Id:N}{extension}";
        return new StoredFile(path, GetDownloadUrl(path, TimeSpan.FromDays(1)));
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        if (!TryParseId(path, out var id)) return;
        var blob = await _db.StoredBlobs.FindAsync(new object[] { id }, ct);
        if (blob is null) return;
        _db.StoredBlobs.Remove(blob);
        await _db.SaveChangesAsync(ct);
    }

    public string? GetDownloadUrl(string path, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string url;
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith('/'))
            url = path;
        else
            url = "/files/" + path.TrimStart('/');

        // Sign db blob URLs so /files/db/{id} is not anonymously enumerable or cross-tenant
        // readable. Signatures do not expire (the id is unguessable and content immutable);
        // the caller's lifetime drives the HTTP cache window on the endpoint instead.
        return FileUrlSigner.SignStoredUrl(url);
    }

    /// <summary>Reads the blob id out of "db/{id}.ext" or "/files/db/{id}.ext".</summary>
    public static bool TryParseId(string? path, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;
        var name = Path.GetFileNameWithoutExtension(path.TrimEnd('/'));
        return Guid.TryParseExact(name, "N", out id) || Guid.TryParse(name, out id);
    }
}
