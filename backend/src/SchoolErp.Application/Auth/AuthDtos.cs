using System.ComponentModel.DataAnnotations;

namespace SchoolErp.Application.Auth;

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(200)] public string Email { get; set; } = string.Empty;
    [Required, MinLength(8), MaxLength(128)] public string Password { get; set; } = string.Empty;
    [MaxLength(30)] public string? SchoolCode { get; set; }
}

public sealed class RefreshRequest
{
    [Required] public string RefreshToken { get; set; } = string.Empty;
}

public sealed class LogoutRequest
{
    [Required] public string RefreshToken { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; set; } = string.Empty;
    [Required, MinLength(8), MaxLength(128)] public string NewPassword { get; set; } = string.Empty;
}

public sealed class UserProfileDto
{
    public int Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string RoleCode { get; init; } = string.Empty;
    public int SchoolId { get; init; }
    public string SchoolName { get; init; } = string.Empty;
    public string? SchoolLogoUrl { get; init; }
    public int? BranchId { get; init; }
    public int? EmployeeId { get; init; }
    public int? GuardianId { get; init; }
    public int? StudentId { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
}

public sealed class AuthResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
    public UserProfileDto User { get; init; } = new();
}
