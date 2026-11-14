using System.ComponentModel.DataAnnotations;

namespace SchoolErp.Application.Common;

/// <summary>Allows only site-relative paths ("/...") or absolute http/https URLs.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class SafeUrlAttribute : ValidationAttribute
{
    public SafeUrlAttribute() : base("{0} must be a relative path starting with '/' or an absolute http/https URL.")
    {
    }

    public static bool IsSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var url = value.Trim();
        if (url.Any(char.IsControl)) return false;
        if (url.StartsWith('/'))
            return !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public override bool IsValid(object? value) => value is null || (value is string s && IsSafe(s));
}
