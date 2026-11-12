using SchoolErp.Domain.Common;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Domain.Entities;

public class SchoolClass : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Stream { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Section : AuditableEntity
{
    public int ClassId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int? ClassTeacherId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Subject : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsElective { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Maps a subject to a class and the teacher who owns it.</summary>
public class ClassSubject : AuditableEntity
{
    public int ClassId { get; set; }
    public int SubjectId { get; set; }
    public int? TeacherId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Teacher scope used to authorize attendance, marks and homework.</summary>
public class TeacherAssignment : AuditableEntity
{
    public int EmployeeId { get; set; }
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public int? SubjectId { get; set; }
    public bool IsClassTeacher { get; set; }
}

public class TimetableEntry : AuditableEntity
{
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public int SubjectId { get; set; }
    public int EmployeeId { get; set; }
    /// <summary>0 = Sunday … 6 = Saturday.</summary>
    public int DayOfWeek { get; set; }
    public int PeriodNo { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string? Room { get; set; }
}

public class Examination : AuditableEntity
{
    public int AcademicSessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ExamType ExamType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ExamStatus Status { get; set; } = ExamStatus.Draft;
    public bool IsRankEnabled { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}

public class ExamSubject : AuditableEntity
{
    public int ExaminationId { get; set; }
    public int ClassId { get; set; }
    public int SubjectId { get; set; }
    public decimal MaxTheoryMarks { get; set; } = 100;
    public decimal MaxPracticalMarks { get; set; }
    public decimal PassMarks { get; set; } = 33;
    public DateOnly? ExamDate { get; set; }
    public TimeSpan? StartTime { get; set; }
}

public class StudentMark : AuditableEntity
{
    public int ExaminationId { get; set; }
    public int ExamSubjectId { get; set; }
    public int StudentId { get; set; }
    public decimal? TheoryMarks { get; set; }
    public decimal? PracticalMarks { get; set; }
    public bool IsAbsent { get; set; }
    public int? EnteredByEmployeeId { get; set; }
    public string? Remarks { get; set; }
}

public class ReportCard : AuditableEntity
{
    public int ExaminationId { get; set; }
    public int StudentId { get; set; }
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public decimal TotalMarks { get; set; }
    public decimal ObtainedMarks { get; set; }
    public decimal Percentage { get; set; }
    public string? Grade { get; set; }
    public int? Rank { get; set; }
    public string ResultStatus { get; set; } = "Pending";
    public DateTime? PublishedAtUtc { get; set; }
}

public class Homework : AuditableEntity
{
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public int SubjectId { get; set; }
    public int EmployeeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AttachmentUrl { get; set; }
    public DateTime DueAtUtc { get; set; }
    public decimal? MaxMarks { get; set; }
    public bool IsPublished { get; set; } = true;
}

public class AssignmentSubmission : AuditableEntity
{
    public int HomeworkId { get; set; }
    public int StudentId { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public string? FileUrl { get; set; }
    public string? Remarks { get; set; }
    public decimal? Marks { get; set; }
    public string? Feedback { get; set; }
    public bool IsLate { get; set; }
}

public class StudyMaterial : AuditableEntity
{
    public int ClassId { get; set; }
    public int? SectionId { get; set; }
    public int SubjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FileUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string? Chapter { get; set; }
    public bool IsPublished { get; set; } = true;
}
