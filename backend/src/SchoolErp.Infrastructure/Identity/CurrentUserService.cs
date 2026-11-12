using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SchoolErp.Application.Common;

namespace SchoolErp.Infrastructure.Identity;

public sealed class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? Principal => _http.HttpContext?.User;

    public int? UserId => ReadInt("sub") ?? ReadInt(ClaimTypes.NameIdentifier);
    public int? SchoolId => ReadInt("school_id");
    public int? BranchId => ReadInt("branch_id");
    public int? EmployeeId => ReadInt("employee_id");
    public int? GuardianId => ReadInt("guardian_id");
    public int? StudentId => ReadInt("student_id");
    public string? Email => Find("email") ?? Find(ClaimTypes.Email);

    public string? IpAddress
    {
        get
        {
            var context = _http.HttpContext;
            if (context is null) return null;

            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first)) return first;
            }

            return context.Connection.RemoteIpAddress?.ToString();
        }
    }

    public string? UserAgent => _http.HttpContext?.Request.Headers.UserAgent.FirstOrDefault();

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Permissions => Principal?
        .FindAll("permission")
        .Select(c => c.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? Array.Empty<string>();

    public IReadOnlyCollection<string> Roles => Principal?
        .FindAll("role")
        .Select(c => c.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? Array.Empty<string>();

    public bool HasPermission(string permission) =>
        Principal?.HasClaim("permission", permission) == true;

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    private string? Find(string type) => Principal?.FindFirst(type)?.Value;

    private int? ReadInt(string type) =>
        int.TryParse(Find(type), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
