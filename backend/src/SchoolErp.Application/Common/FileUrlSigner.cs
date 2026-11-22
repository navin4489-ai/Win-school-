using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SchoolErp.Application.Common;

/// <summary>
/// Signs and validates download URLs served under <c>/files</c>: database-backed blobs at
/// <c>/files/db/{id}</c> and local-disk uploads at <c>/files/{school-folder}/...</c>.
/// Signatures never expire: the link only needs to prove it was minted by the server
/// (preventing anonymous path guessing / cross-tenant reads), not to time-limit it.
///
/// The signature is <c>HMAC-SHA256(secret, canonical-path-without-sig)</c>, truncated to
/// 16 bytes and rendered as 32 lower-case hex characters. The canonical input is the
/// request path only (e.g. <c>/files/db/{guid}.jpg</c> or
/// <c>/files/school-2/2026/11/{guid}.png</c>), with any existing <c>sig</c> and query string
/// removed, so a URL signs and validates to the same value regardless of how it was passed
/// in (stored path <c>db/{guid}.jpg</c> or absolute path <c>/files/db/...</c>).
///
/// Configured once at start-up through <see cref="Initialize"/>. The secret is kept in
/// memory and never logged.
/// </summary>
/// <summary>
/// Settings for signed file URLs, bound from the <c>Files</c> configuration section.
/// </summary>
public sealed class FileSigningOptions
{
    public const string SectionName = "Files";

    /// <summary>HMAC secret. When empty the app falls back to <c>Jwt:SigningKey</c>.</summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Grace cut-off (ISO/UTC). While <c>DateTime.UtcNow</c> is before this instant,
    /// unsigned requests to <c>/files/*</c> are still served so URLs already cached
    /// in browsers/localStorage keep working. After it, unsigned requests are denied.
    /// When null, unsigned requests are denied outright (secure by default).
    /// </summary>
    public DateTime? SignedFromUtc { get; set; }
}

public static class FileUrlSigner
{
    private const string FilesRoutePrefix = "/files/";
    private const string RoutePrefix = "/files/db/";
    private const int SignatureBytes = 16; // 32 hex chars

    private static byte[]? _secret;

    /// <summary>
    /// Supplies the HMAC secret. Call once at start-up. The secret must be non-empty;
    /// callers resolve it from <c>Files:Secret</c> and fall back to <c>Jwt:SigningKey</c>.
    /// </summary>
    public static void Initialize(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "FileUrlSigner requires a non-empty secret (Files:Secret or Jwt:SigningKey).");
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>True once <see cref="Initialize"/> has been called.</summary>
    public static bool IsConfigured => _secret is not null;

    /// <summary>
    /// Returns true when <paramref name="value"/> points at a served file: a db blob
    /// (<c>db/{guid}</c> or <c>/files/db/{guid}</c>) or a local upload
    /// (<c>/files/school-{id}/yyyy/MM/{name}</c> / <c>/files/shared/...</c>).
    /// </summary>
    public static bool IsSignablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var path = ToCanonicalPath(value);
        return path is not null;
    }

    /// <summary>
    /// Idempotently signs a stored file URL. Returns the value unchanged when it is not a
    /// served-file path or is already signed; otherwise appends <c>?sig=...</c>. Safe to
    /// apply anywhere a stored URL is copied into a response DTO.
    /// </summary>
    public static string? SignStoredUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (ContainsSig(value)) return value; // never double-sign
        var canonical = ToCanonicalPath(value);
        if (canonical is null) return value; // not a served file; leave as-is

        var sig = Compute(canonical);
        // Preserve the caller's original form (stored "db/..." vs "/files/db/...",
        // or a relative "/files/school-.../..." URL from local storage).
        return value.TrimEnd() + "?sig=" + sig;
    }

    /// <summary>
    /// Validates a signature against a request path. <paramref name="pathWithoutQuery"/>
    /// is the request path (no query string); <paramref name="sig"/> is the supplied value.
    /// Constant-time comparison. Returns false for non-db paths.
    /// </summary>
    public static bool Validate(string? pathWithoutQuery, string? sig)
    {
        if (string.IsNullOrWhiteSpace(sig)) return false;
        var canonical = ToCanonicalPath(pathWithoutQuery);
        if (canonical is null) return false;

        var expected = Compute(canonical);
        var a = Encoding.ASCII.GetBytes(expected);
        var b = Encoding.ASCII.GetBytes(sig.Trim().ToLowerInvariant());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Computes the raw signature for an already-canonical path. Exposed for tooling/tests.</summary>
    public static string Compute(string canonicalPath)
    {
        var secret = _secret ?? throw new InvalidOperationException("FileUrlSigner is not initialized.");
        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPath));
        var sb = new StringBuilder(SignatureBytes * 2);
        for (var i = 0; i < SignatureBytes; i++)
            sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// Reduces any accepted form to the canonical signing input: <c>/files/db/{file}</c>
    /// for blobs or <c>/files/{school-folder}/.../{file}</c> for local uploads. Returns
    /// null for anything else (including traversal segments). Strips scheme/host, query
    /// and fragment.
    /// </summary>
    public static string? ToCanonicalPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();

        // Drop query and fragment.
        var cut = v.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) v = v[..cut];

        // Drop scheme://host if absolute.
        var schemeIdx = v.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx >= 0)
        {
            var afterScheme = v[(schemeIdx + 3)..];
            var slash = afterScheme.IndexOf('/');
            v = slash < 0 ? "/" : afterScheme[slash..];
        }

        // Normalize stored form "db/{file}" to "/files/db/{file}".
        if (v.StartsWith("db/", StringComparison.OrdinalIgnoreCase))
            v = FilesRoutePrefix + v;
        if (!v.StartsWith('/')) v = "/" + v;

        if (v.StartsWith(RoutePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var file = v[RoutePrefix.Length..];
            return file.Length == 0 || file.Contains('/') ? null : RoutePrefix + file;
        }

        if (!v.StartsWith(FilesRoutePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var relative = v[FilesRoutePrefix.Length..];
        var segments = relative.Split('/');
        if (segments.Length < 2) return null; // a folder and a file are required
        var rootFolder = segments[0];
        if (!rootFolder.Equals("shared", StringComparison.OrdinalIgnoreCase) &&
            !rootFolder.StartsWith("school-", StringComparison.OrdinalIgnoreCase)) return null;
        if (segments.Any(s => s.Length == 0 || s is "." or ".." || s.Contains('\\') || s.Contains(':')))
            return null;
        return FilesRoutePrefix + string.Join('/', segments);
    }

    private static bool ContainsSig(string value) =>
        value.Contains("?sig=", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("&sig=", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The one access rule shared by the DB and local-disk <c>/files/*</c> routes: a valid
/// HMAC for the exact request path, an authenticated caller, or the legacy grace window.
/// </summary>
public static class FileAccessPolicy
{
    public static bool IsAllowed(string? pathWithoutQuery, string? sig, bool authenticated,
        DateTime? signedFromUtc, DateTime? utcNow = null)
    {
        if (FileUrlSigner.Validate(pathWithoutQuery, sig)) return true;
        if (authenticated) return true;
        return signedFromUtc.HasValue && (utcNow ?? DateTime.UtcNow) < signedFromUtc.Value;
    }
}
