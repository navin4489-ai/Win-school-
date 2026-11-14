using System.ComponentModel.DataAnnotations;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Exams;

public sealed class ExamDto
{
    public int Id { get; init; }
    public int AcademicSessionId { get; init; }
    public string Name { get; init; } = string.Empty;
    public ExamType ExamType { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public ExamStatus Status { get; init; }
    public bool IsRankEnabled { get; init; }
    public int SubjectCount { get; init; }
}

public sealed class SaveExamRequest
{
    [Range(1, int.MaxValue)] public int AcademicSessionId { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public ExamType ExamType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsRankEnabled { get; set; }
}

public sealed class UpdateExamRequest
{
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public ExamType ExamType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsRankEnabled { get; set; }
}

public sealed class ExamSubjectDto
{
    public int Id { get; init; }
    public int ClassId { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public int SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public decimal MaxTheoryMarks { get; init; }
    public decimal MaxPracticalMarks { get; init; }
    public decimal PassMarks { get; init; }
    public DateOnly? ExamDate { get; init; }
}

public sealed class SaveExamSubjectRequest
{
    [Range(1, int.MaxValue)] public int ClassId { get; set; }
    [Range(1, int.MaxValue)] public int SubjectId { get; set; }
    [Range(0, 1000)] public decimal MaxTheoryMarks { get; set; } = 100;
    [Range(0, 1000)] public decimal MaxPracticalMarks { get; set; }
    [Range(0, 1000)] public decimal PassMarks { get; set; } = 33;
    public DateOnly? ExamDate { get; set; }
    public TimeSpan? StartTime { get; set; }
}

public sealed class MarkInput
{
    [Range(1, int.MaxValue)] public int StudentId { get; set; }
    [Range(0, 1000)] public decimal? TheoryMarks { get; set; }
    [Range(0, 1000)] public decimal? PracticalMarks { get; set; }
    public bool IsAbsent { get; set; }
    [MaxLength(250)] public string? Remarks { get; set; }
}

public sealed class SaveMarksRequest
{
    [Range(1, int.MaxValue)] public int ExamSubjectId { get; set; }
    [MinLength(1)] public List<MarkInput> Marks { get; set; } = new();
}

public sealed class StudentMarkDto
{
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string AdmissionNo { get; init; } = string.Empty;
    public decimal? TheoryMarks { get; init; }
    public decimal? PracticalMarks { get; init; }
    public bool IsAbsent { get; init; }
    public string? Remarks { get; init; }
}

public sealed class ExamResultRowDto
{
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string AdmissionNo { get; init; } = string.Empty;
    public decimal TotalMarks { get; init; }
    public decimal ObtainedMarks { get; init; }
    public decimal Percentage { get; init; }
    public string? Grade { get; init; }
    public string ResultStatus { get; init; } = string.Empty;
    public int? Rank { get; init; }
}

public sealed record ReportCardDto
{
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string AdmissionNo { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string SectionName { get; init; } = string.Empty;
    public string ExaminationName { get; init; } = string.Empty;
    public decimal TotalMarks { get; init; }
    public decimal ObtainedMarks { get; init; }
    public decimal Percentage { get; init; }
    public string? Grade { get; init; }
    public int? Rank { get; init; }
    public string ResultStatus { get; init; } = string.Empty;
    public IReadOnlyList<SubjectMarkDto> Subjects { get; init; } = Array.Empty<SubjectMarkDto>();
    public string? Html { get; init; }
}

public sealed class SubjectMarkDto
{
    public string SubjectName { get; init; } = string.Empty;
    public decimal MaxMarks { get; init; }
    public decimal? TheoryMarks { get; init; }
    public decimal? PracticalMarks { get; init; }
    public decimal ObtainedMarks { get; init; }
    public bool IsAbsent { get; init; }
    public string? Grade { get; init; }
}
