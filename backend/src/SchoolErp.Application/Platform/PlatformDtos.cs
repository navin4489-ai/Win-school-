using System.ComponentModel.DataAnnotations;

namespace SchoolErp.Application.Platform;

public sealed class ModuleDto
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class SchoolSummaryDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? LegalName { get; init; }
    public string? LogoUrl { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? AddressLine1 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<string> EnabledModules { get; init; } = Array.Empty<string>();
    public int UserCount { get; init; }
    public int StudentCount { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public class SaveSchoolRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(200)] public string? LegalName { get; set; }
    [MaxLength(512)] public string? LogoUrl { get; set; }
    [EmailAddress, MaxLength(256)] public string? Email { get; set; }
    [MaxLength(32)] public string? Phone { get; set; }
    [MaxLength(2000)] public string? AddressLine1 { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(100)] public string? State { get; set; }
    [MaxLength(20)] public string? PostalCode { get; set; }
    public List<string> EnabledModules { get; set; } = new();
}

public sealed class CreateSchoolRequest : SaveSchoolRequest
{
    /// <summary>Short unique code used at sign-in and on receipt numbers.</summary>
    [Required, RegularExpression("^[A-Za-z0-9-]{2,20}$",
        ErrorMessage = "The school code must be 2–20 letters, digits or hyphens.")]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(200)] public string AdminFullName { get; set; } = string.Empty;
    [Required, EmailAddress, MaxLength(256)] public string AdminEmail { get; set; } = string.Empty;
    [Required, MinLength(8), MaxLength(100)] public string AdminPassword { get; set; } = string.Empty;
}

public sealed class SetActiveRequest
{
    public bool IsActive { get; set; }
}

public sealed class RoleOptionDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int PermissionCount { get; init; }
}

public sealed class UserDto
{
    public int Id { get; init; }
    public int SchoolId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public int RoleId { get; init; }
    public string RoleCode { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime? LastLoginAtUtc { get; init; }
    public bool IsLockedOut { get; init; }
}

public sealed class CreateUserRequest
{
    /// <summary>Only the super admin may target another school; everyone else uses their own.</summary>
    public int? SchoolId { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = string.Empty;
    [MaxLength(32)] public string? Phone { get; set; }
    [Range(1, int.MaxValue)] public int RoleId { get; set; }
    [Required, MinLength(8), MaxLength(100)] public string Password { get; set; } = string.Empty;
}

public sealed class UpdateUserRequest
{
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(32)] public string? Phone { get; set; }
    [Range(1, int.MaxValue)] public int RoleId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ResetPasswordRequest
{
    [Required, MinLength(8), MaxLength(100)] public string NewPassword { get; set; } = string.Empty;
}
