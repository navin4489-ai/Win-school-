using System.ComponentModel.DataAnnotations;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Admissions;

public sealed class AdmissionDto
{
    public int Id { get; init; }
    public string ApplicationNo { get; init; } = string.Empty;
    public int AcademicSessionId { get; init; }
    public int? ClassId { get; init; }
    public string? ClassName { get; init; }
    public int? SectionId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public DateOnly DateOfBirth { get; init; }
    public Gender Gender { get; init; }
    public string GuardianName { get; init; } = string.Empty;
    public string GuardianPhone { get; init; } = string.Empty;
    public string? GuardianEmail { get; init; }
    public string? PreviousSchool { get; init; }
    public AdmissionStatus Status { get; init; }
    public DateTime AppliedAtUtc { get; init; }
    public DateTime? InterviewAtUtc { get; init; }
    public string? RejectionReason { get; init; }
    public int? StudentId { get; init; }
    public string? Notes { get; init; }
    public string? Source { get; init; }
    public bool IsFeePaid { get; init; }
}

public sealed class AdmissionQuery : PageQuery
{
    public string? Search { get; set; }
    public AdmissionStatus? Status { get; set; }
    public int? SessionId { get; set; }
    public int? ClassId { get; set; }
}

public sealed class CreateEnquiryRequest
{
    [Required, MaxLength(80)] public string FirstName { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    [Required, MaxLength(120)] public string GuardianName { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string GuardianPhone { get; set; } = string.Empty;
    [EmailAddress, MaxLength(200)] public string? GuardianEmail { get; set; }
    [Range(1, int.MaxValue)] public int? ClassId { get; set; }
    [Range(1, int.MaxValue)] public int? SessionId { get; set; }
    [MaxLength(250)] public string? PreviousSchool { get; set; }
    [MaxLength(50)] public string? Source { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
}

public sealed class ScheduleInterviewRequest
{
    public DateTime InterviewAtUtc { get; set; }
}

public sealed class RejectAdmissionRequest
{
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
}

public sealed class EnrollAdmissionRequest
{
    [Range(1, int.MaxValue)] public int SectionId { get; set; }
    [MaxLength(30)] public string? AdmissionNo { get; set; }
    [MaxLength(20)] public string? RollNo { get; set; }
    public bool CreateParentLogin { get; set; } = true;
    [MinLength(8), MaxLength(128)] public string? ParentPassword { get; set; }
    [Range(0, 10_000_000)] public decimal? AdmissionFeeAmount { get; set; }
}

public sealed class EnrollmentResult
{
    public int StudentId { get; init; }
    public string AdmissionNo { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;
    public int? GuardianId { get; init; }
    public int? ParentUserAccountId { get; init; }
    public string? ParentEmail { get; init; }
    public bool ParentLoginCreated { get; init; }
    /// <summary>Returned only once, when a parent login was created without a supplied password.</summary>
    public string? TemporaryPassword { get; init; }
}
