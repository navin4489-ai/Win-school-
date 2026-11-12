using System.ComponentModel.DataAnnotations;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Students;

public sealed class StudentQuery : PageQuery
{
    public string? Search { get; set; }
    public int? ClassId { get; set; }
    public int? SectionId { get; set; }
    public int? SessionId { get; set; }
    public StudentStatus? Status { get; set; }
}

public sealed class StudentListItemDto
{
    public int Id { get; init; }
    public string AdmissionNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string SectionName { get; init; } = string.Empty;
    public string? RollNo { get; init; }
    public string? Phone { get; init; }
    public string? PhotoUrl { get; init; }
    public StudentStatus Status { get; init; }
    public DateOnly AdmissionDate { get; init; }
}

public sealed class GuardianDto
{
    public int Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Relation { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsEmergencyContact { get; init; }
}

public sealed class StudentDetailDto
{
    public int Id { get; init; }
    public string AdmissionNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public int AcademicSessionId { get; init; }
    public int ClassId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public int SectionId { get; init; }
    public string SectionName { get; init; } = string.Empty;
    public string? RollNo { get; init; }
    public DateOnly DateOfBirth { get; init; }
    public Gender Gender { get; init; }
    public string? BloodGroup { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public DateOnly AdmissionDate { get; init; }
    public StudentStatus Status { get; init; }
    public string? PhotoUrl { get; init; }
    public string? PreviousSchool { get; init; }
    public IReadOnlyList<GuardianDto> Guardians { get; init; } = Array.Empty<GuardianDto>();
    public IReadOnlyList<EnrollmentDto> Enrollments { get; init; } = Array.Empty<EnrollmentDto>();
    public IReadOnlyList<StudentDocumentDto> Documents { get; init; } = Array.Empty<StudentDocumentDto>();
}

public sealed class EnrollmentDto
{
    public int Id { get; init; }
    public int AcademicSessionId { get; init; }
    public string SessionName { get; init; } = string.Empty;
    public int ClassId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public int SectionId { get; init; }
    public string SectionName { get; init; } = string.Empty;
    public string? RollNo { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class StudentDocumentDto
{
    public int Id { get; init; }
    public int DocumentId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DocumentType DocumentType { get; init; }
    public bool IsVerified { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string? Url { get; init; }
}

public sealed class UpdateStudentRequest
{
    [Required, MaxLength(80)] public string FirstName { get; set; } = string.Empty;
    [MaxLength(80)] public string? MiddleName { get; set; }
    [Required, MaxLength(80)] public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    [MaxLength(10)] public string? BloodGroup { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(20)] public string? Phone { get; set; }
    [MaxLength(300)] public string? Address { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(100)] public string? State { get; set; }
    [MaxLength(20)] public string? PostalCode { get; set; }
    [MaxLength(500)] public string? MedicalNotes { get; set; }
    [MaxLength(250)] public string? PreviousSchool { get; set; }
    [Url, MaxLength(500)] public string? PhotoUrl { get; set; }
}

public sealed class PromoteStudentRequest
{
    [Range(1, int.MaxValue)] public int AcademicSessionId { get; set; }
    [Range(1, int.MaxValue)] public int ClassId { get; set; }
    [Range(1, int.MaxValue)] public int SectionId { get; set; }
    [MaxLength(20)] public string? RollNo { get; set; }
    public bool AssignNextClassFee { get; set; }
}

public sealed class WithdrawStudentRequest
{
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    public DateOnly? WithdrawalDate { get; set; }
}

public sealed class CertificateDto
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "text/html";
    public string Content { get; init; } = string.Empty;
}
