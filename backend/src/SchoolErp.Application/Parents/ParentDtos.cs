using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Parents;

public sealed class ChildDto
{
    public int StudentId { get; init; }
    public string AdmissionNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string SectionName { get; init; } = string.Empty;
    public string? PhotoUrl { get; init; }
    public string? RollNo { get; init; }
}

public sealed class ChildOverviewDto
{
    public ChildDto Child { get; init; } = new();
    public AttendanceSummaryDto Attendance { get; init; } = new();
    public FeeSummaryDto Fees { get; init; } = new();
    public IReadOnlyList<ResultSummaryDto> RecentResults { get; init; } = Array.Empty<ResultSummaryDto>();
    public IReadOnlyList<HomeworkSummaryDto> PendingHomework { get; init; } = Array.Empty<HomeworkSummaryDto>();
    public IReadOnlyList<AnnouncementDto> Announcements { get; init; } = Array.Empty<AnnouncementDto>();
}

public sealed class AttendanceSummaryDto
{
    public int TotalDays { get; init; }
    public int Present { get; init; }
    public int Absent { get; init; }
    public int Late { get; init; }
    public int HalfDay { get; init; }
    public decimal Percentage { get; init; }
}

public sealed class FeeSummaryDto
{
    public decimal TotalDue { get; init; }
    public decimal TotalPaid { get; init; }
    public decimal Balance { get; init; }
    public decimal Overdue { get; init; }
    public IReadOnlyList<InstallmentSummaryDto> Installments { get; init; } = Array.Empty<InstallmentSummaryDto>();
}

public sealed class InstallmentSummaryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly DueDate { get; init; }
    public decimal Amount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal Balance { get; init; }
    public InstallmentStatus Status { get; init; }
}

public sealed class ResultSummaryDto
{
    public int ExaminationId { get; init; }
    public string ExaminationName { get; init; } = string.Empty;
    public decimal Percentage { get; init; }
    public string? Grade { get; init; }
    public string ResultStatus { get; init; } = string.Empty;
    public int? Rank { get; init; }
}

public sealed class HomeworkSummaryDto
{
    public int Id { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public DateTime DueAtUtc { get; init; }
    public bool Submitted { get; init; }
}

public sealed class AnnouncementDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public DateTime? PublishedAtUtc { get; init; }
}
