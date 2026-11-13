using System.ComponentModel.DataAnnotations;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Attendance;

public sealed class RosterEntryDto
{
    public int StudentId { get; init; }
    public string AdmissionNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? RollNo { get; init; }
    public AttendanceStatus? Status { get; init; }
    public string? Remarks { get; init; }
}

public sealed class ClassAttendanceDto
{
    public int ClassId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public int SectionId { get; init; }
    public string SectionName { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public bool IsSubmitted { get; init; }
    public IReadOnlyList<RosterEntryDto> Students { get; init; } = Array.Empty<RosterEntryDto>();
}

public sealed class AttendanceEntryRequest
{
    [Range(1, int.MaxValue)] public int StudentId { get; set; }
    public AttendanceStatus Status { get; set; }
    [MaxLength(250)] public string? Remarks { get; set; }
}

public sealed class MarkAttendanceRequest
{
    [Range(1, int.MaxValue)] public int ClassId { get; set; }
    [Range(1, int.MaxValue)] public int SectionId { get; set; }
    public DateOnly Date { get; set; }
    [MinLength(1)] public List<AttendanceEntryRequest> Entries { get; set; } = new();
}

public sealed class MarkAttendanceResult
{
    public int Created { get; init; }
    public int Updated { get; init; }
    public int NotificationsQueued { get; init; }
}

public sealed class StudentAttendanceRecordDto
{
    public DateOnly Date { get; init; }
    public AttendanceStatus Status { get; init; }
    public string? Remarks { get; init; }
}

public sealed class StudentAttendanceSummaryDto
{
    public int StudentId { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public int TotalDays { get; init; }
    public int Present { get; init; }
    public int Absent { get; init; }
    public int Late { get; init; }
    public int HalfDay { get; init; }
    public decimal Percentage { get; init; }
    public IReadOnlyList<StudentAttendanceRecordDto> Records { get; init; } = Array.Empty<StudentAttendanceRecordDto>();
}

public sealed class StaffAttendanceDto
{
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public DateTime? CheckInUtc { get; init; }
    public DateTime? CheckOutUtc { get; init; }
    public AttendanceStatus Status { get; init; }
    public bool IsLate { get; init; }
    public string? CheckInPhotoUrl { get; init; }
    public string? CheckOutPhotoUrl { get; init; }
    public decimal? CheckInLatitude { get; init; }
    public decimal? CheckInLongitude { get; init; }
    public decimal? CheckOutLatitude { get; init; }
    public decimal? CheckOutLongitude { get; init; }
    public string? CheckInAddress { get; init; }
    public string? CheckOutAddress { get; init; }
}

public sealed class CheckInRequest
{
    public int? EmployeeId { get; set; }
    public AttendanceSource Source { get; set; } = AttendanceSource.Manual;

    /// <summary>Public URL of the photo captured from the device camera.</summary>
    [MaxLength(512)] public string? PhotoUrl { get; set; }

    [Range(-90, 90)] public double? Latitude { get; set; }

    [Range(-180, 180)] public double? Longitude { get; set; }

    [MaxLength(250)] public string? Address { get; set; }
}

public sealed class StaffAttendanceRangeDto
{
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public int TotalDays { get; init; }
    public int Present { get; init; }
    public int Absent { get; init; }
    public int Late { get; init; }
    public decimal HoursWorked { get; init; }
    public IReadOnlyList<StaffAttendanceDto> Records { get; init; } = Array.Empty<StaffAttendanceDto>();
}
