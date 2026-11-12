using SchoolErp.Application.Attendance;

namespace SchoolErp.Application.Dashboard;

public sealed class DashboardCardDto
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public string? Hint { get; init; }
    public string Tone { get; init; } = "neutral";
}

public sealed class ChartPointDto
{
    public string Label { get; init; } = string.Empty;
    public decimal Value { get; init; }
}

public sealed class ActivityDto
{
    public string Title { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public DateTime AtUtc { get; init; }
    public string Tone { get; init; } = "neutral";
}

public sealed class DashboardDto
{
    public string Role { get; init; } = string.Empty;
    public string? AcademicSession { get; init; }
    public IReadOnlyList<DashboardCardDto> Cards { get; init; } = Array.Empty<DashboardCardDto>();
    public IReadOnlyList<ChartPointDto> FeeCollectionTrend { get; init; } = Array.Empty<ChartPointDto>();
    public IReadOnlyList<ChartPointDto> AttendanceTrend { get; init; } = Array.Empty<ChartPointDto>();
    public IReadOnlyList<ChartPointDto> AdmissionTrend { get; init; } = Array.Empty<ChartPointDto>();
    public IReadOnlyList<ActivityDto> RecentActivity { get; init; } = Array.Empty<ActivityDto>();
    public IReadOnlyList<TimetableSlotDto> TodayTimetable { get; init; } = Array.Empty<TimetableSlotDto>();
}

public sealed class TimetableSlotDto
{
    public string Period { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Class { get; init; } = string.Empty;
    public string? Room { get; init; }
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
}
