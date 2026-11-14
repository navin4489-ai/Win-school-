using System.ComponentModel.DataAnnotations;

namespace SchoolErp.Application.Administration;

public sealed class SchoolDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? LegalName { get; init; }
    public string? LogoUrl { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string Country { get; init; } = string.Empty;
    public string TimeZone { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = string.Empty;
}

public sealed class UpdateSchoolRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(200)] public string? LegalName { get; set; }
    [Url, MaxLength(500)] public string? LogoUrl { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(250)] public string? AddressLine1 { get; set; }
    [MaxLength(250)] public string? AddressLine2 { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(100)] public string? State { get; set; }
    [MaxLength(20)] public string? PostalCode { get; set; }
    [MaxLength(100)] public string? Country { get; set; }
    [MaxLength(60)] public string? TimeZone { get; set; }
    [MaxLength(3)] public string? CurrencyCode { get; set; }
}

public sealed class BranchDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public bool IsMainBranch { get; init; }
}

public sealed class SaveBranchRequest
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [MaxLength(300)] public string? Address { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    public bool IsMainBranch { get; set; }
}

public sealed class AcademicSessionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsClosed { get; init; }
}

public sealed class SaveAcademicSessionRequest
{
    [Required, MaxLength(50)] public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class ClassDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Stream { get; init; }
    public int DisplayOrder { get; init; }
}

public sealed class SaveClassRequest
{
    [Required, MaxLength(50)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Code { get; set; } = string.Empty;
    [MaxLength(50)] public string? Stream { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class SectionDto
{
    public int Id { get; init; }
    public int ClassId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Capacity { get; init; }
    public int? ClassTeacherId { get; init; }
    public int StudentCount { get; init; }
}

public sealed class SaveSectionRequest
{
    [Required, MaxLength(20)] public string Name { get; set; } = string.Empty;
    [Range(1, 500)] public int Capacity { get; set; } = 40;
    public int? ClassTeacherId { get; set; }
}

/// <summary>A teacher's scope over a class, section and (optionally) a subject.</summary>
public sealed class TeacherAssignmentDto
{
    public int Id { get; init; }
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;
    public int ClassId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public int SectionId { get; init; }
    public string SectionName { get; init; } = string.Empty;
    public int? SubjectId { get; init; }
    public string? SubjectName { get; init; }
    public bool IsClassTeacher { get; init; }
}

public sealed class SaveTeacherAssignmentRequest
{
    [Range(1, int.MaxValue)] public int EmployeeId { get; set; }
    [Range(1, int.MaxValue)] public int ClassId { get; set; }
    [Range(1, int.MaxValue)] public int SectionId { get; set; }
    public int? SubjectId { get; set; }
    public bool IsClassTeacher { get; set; }
}

public sealed class SubjectDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public bool IsElective { get; init; }
}

public sealed class SaveSubjectRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Code { get; set; } = string.Empty;
    public bool IsElective { get; set; }
}

public sealed class ClassSubjectDto
{
    public int Id { get; init; }
    public int ClassId { get; init; }
    public int SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public int? TeacherId { get; init; }
    public string? TeacherName { get; init; }
}

public sealed class SaveClassSubjectRequest
{
    [Range(1, int.MaxValue)] public int SubjectId { get; set; }
    public int? TeacherId { get; set; }
}

public sealed class RoleDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsSystem { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
}

public sealed class PermissionDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public sealed class UpdateRolePermissionsRequest
{
    [Required] public List<int> PermissionIds { get; set; } = new();
}

public sealed class TimetableEntryDto
{
    public int Id { get; init; }
    public int ClassId { get; init; }
    public int SectionId { get; init; }
    public int SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public int EmployeeId { get; init; }
    public string TeacherName { get; init; } = string.Empty;
    public int DayOfWeek { get; init; }
    public int PeriodNo { get; init; }
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
    public string? Room { get; init; }
}

public sealed class SaveTimetableEntryRequest
{
    [Range(1, int.MaxValue)] public int ClassId { get; set; }
    [Range(1, int.MaxValue)] public int SectionId { get; set; }
    [Range(1, int.MaxValue)] public int SubjectId { get; set; }
    [Range(1, int.MaxValue)] public int EmployeeId { get; set; }
    [Range(0, 6)] public int DayOfWeek { get; set; }
    [Range(1, 20)] public int PeriodNo { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    [MaxLength(50)] public string? Room { get; set; }
}

public sealed class AnnouncementDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public DateTime? PublishedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class SaveAnnouncementRequest
{
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(4000)] public string Body { get; set; } = string.Empty;
    [MaxLength(30)] public string Audience { get; set; } = "All";
    public DateTime? ExpiresAtUtc { get; set; }
}
