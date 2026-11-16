using System.ComponentModel.DataAnnotations;

namespace SchoolErp.Application.Homework;

public sealed class HomeworkDto
{
    public int Id { get; init; }
    public int ClassId { get; init; }
    public int SectionId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public string TeacherName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? AttachmentUrl { get; init; }
    public DateTime DueAtUtc { get; init; }
    public decimal? MaxMarks { get; init; }
    public int SubmissionCount { get; init; }
    public bool IsOverdue { get; init; }
    public bool IsPublished { get; init; }
    public int EmployeeId { get; init; }
}

public sealed class SaveHomeworkRequest
{
    [Range(1, int.MaxValue)] public int ClassId { get; set; }
    [Range(1, int.MaxValue)] public int SectionId { get; set; }
    [Range(1, int.MaxValue)] public int SubjectId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Description { get; set; }
    [Url, MaxLength(500)] public string? AttachmentUrl { get; set; }
    public DateTime DueAtUtc { get; set; }
    [Range(0, 1000)] public decimal? MaxMarks { get; set; }
    public bool IsPublished { get; set; } = true;
}

public sealed class UpdateHomeworkRequest
{
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Description { get; set; }
    [Url, MaxLength(500)] public string? AttachmentUrl { get; set; }
    public DateTime DueAtUtc { get; set; }
    [Range(0, 1000)] public decimal? MaxMarks { get; set; }
    public bool IsPublished { get; set; } = true;
}

public sealed class SubmissionDto
{
    public int Id { get; init; }
    public int HomeworkId { get; init; }
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public DateTime SubmittedAtUtc { get; init; }
    public string? FileUrl { get; set; }
    public string? Remarks { get; init; }
    public decimal? Marks { get; init; }
    public string? Feedback { get; init; }
    public bool IsLate { get; init; }
}

public sealed class SubmitHomeworkRequest
{
    [Url, MaxLength(500)] public string? FileUrl { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
}

public sealed class GradeSubmissionRequest
{
    [Range(0, 1000)] public decimal Marks { get; set; }
    [MaxLength(1000)] public string? Feedback { get; set; }
}
