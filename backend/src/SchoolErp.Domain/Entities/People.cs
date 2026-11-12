using System.ComponentModel.DataAnnotations.Schema;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Domain.Entities;

public class Student : AuditableEntity
{
    public string AdmissionNo { get; set; } = string.Empty;
    public int AcademicSessionId { get; set; }
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public string? RollNo { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public string? BloodGroup { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public DateOnly AdmissionDate { get; set; }
    public StudentStatus Status { get; set; } = StudentStatus.Active;
    public string? PhotoUrl { get; set; }
    /// <summary>Restricted: only nurse/principal/admin permissions should surface this.</summary>
    public string? MedicalNotes { get; set; }
    public string? PreviousSchool { get; set; }
    public string? WithdrawalReason { get; set; }
    public DateOnly? WithdrawalDate { get; set; }

    [NotMapped]
    public string FullName => string.Join(' ', new[] { FirstName, MiddleName, LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

/// <summary>Immutable year-by-year placement history, preserved across promotion.</summary>
public class StudentEnrollment : AuditableEntity
{
    public int StudentId { get; set; }
    public int AcademicSessionId { get; set; }
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public string? RollNo { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Status { get; set; } = "Active";
    public string? Remarks { get; set; }
}

public class Guardian : AuditableEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Relation { get; set; } = "Father";
    public string? Email { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Occupation { get; set; }
    public string? Address { get; set; }
    public int? UserAccountId { get; set; }
}

public class StudentParent : AuditableEntity
{
    public int StudentId { get; set; }
    public int GuardianId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsEmergencyContact { get; set; }
}

public class Admission : AuditableEntity
{
    public string ApplicationNo { get; set; } = string.Empty;
    public int AcademicSessionId { get; set; }
    public int? ClassId { get; set; }
    public int? SectionId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public string GuardianName { get; set; } = string.Empty;
    public string GuardianPhone { get; set; } = string.Empty;
    public string? GuardianEmail { get; set; }
    public string? PreviousSchool { get; set; }
    public AdmissionStatus Status { get; set; } = AdmissionStatus.Enquiry;
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? InterviewAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int? ApprovedBy { get; set; }
    public string? RejectionReason { get; set; }
    public int? StudentId { get; set; }
    public string? Notes { get; set; }
    public string? Source { get; set; }
    public bool IsFeePaid { get; set; }
}

public class Employee : AuditableEntity
{
    public string EmployeeNo { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public int? DepartmentId { get; set; }
    public int? DesignationId { get; set; }
    public DateOnly JoiningDate { get; set; }
    public DateOnly? ExitDate { get; set; }
    public EmploymentStatus Status { get; set; } = EmploymentStatus.Active;
    public string? Qualification { get; set; }
    public decimal? ExperienceYears { get; set; }
    public string? Address { get; set; }
    public string? PhotoUrl { get; set; }
    public decimal? BasicSalary { get; set; }
    [NotMapped]
    public string FullName => $"{FirstName} {LastName}".Trim();
}

public class Department : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int? HeadEmployeeId { get; set; }
}

public class Designation : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int? Level { get; set; }
}

public class UserAccount : AuditableEntity
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public int RoleId { get; set; }
    public int? EmployeeId { get; set; }
    public int? GuardianId { get; set; }
    public int? StudentId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public int TokenVersion { get; set; } = 1;
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetExpiresAtUtc { get; set; }
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiresAtUtc { get; set; }
}

public class Role : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
}

public class Permission : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class RolePermission : Entity
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
}

public class RefreshToken : Entity
{
    public int UserAccountId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? RevokedReason { get; set; }
    [NotMapped]
    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;
}

public class LeaveApplication : AuditableEntity
{
    public int EmployeeId { get; set; }
    public string LeaveType { get; set; } = "Casual";
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? Remarks { get; set; }
}
