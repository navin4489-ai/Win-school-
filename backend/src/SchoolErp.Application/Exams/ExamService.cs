using System.Net;
using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Exams;

public static class GradingCalculator
{
    /// <summary>CBSE-style default scale. Schools can override this in a later release.</summary>
    public static string Grade(decimal percentage) => percentage switch
    {
        >= 91 => "A1",
        >= 81 => "A2",
        >= 71 => "B1",
        >= 61 => "B2",
        >= 51 => "C1",
        >= 41 => "C2",
        >= 33 => "D",
        _ => "E"
    };
}

public sealed class ExamService
{
    private static readonly HashSet<string> AdminRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleCodes.SuperAdmin, RoleCodes.SchoolOwner, RoleCodes.Principal, RoleCodes.Administrator
    };

    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IDateTimeProvider _clock;

    public ExamService(IAppDbContext db, IUnitOfWork uow, ICurrentUser user, IDateTimeProvider clock)
    {
        _db = db;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    public async Task<List<ExamDto>> ListAsync(int? sessionId, CancellationToken ct = default)
    {
        var query = _db.Examinations.Where(e => e.SchoolId == SchoolId && !e.IsDeleted);
        if (sessionId is > 0) query = query.Where(e => e.AcademicSessionId == sessionId);

        var exams = await query.OrderByDescending(e => e.StartDate).ToListAsync(ct);
        var ids = exams.Select(e => e.Id).ToList();
        var counts = await _db.ExamSubjects.Where(s => ids.Contains(s.ExaminationId) && !s.IsDeleted)
            .GroupBy(s => s.ExaminationId)
            .Select(g => new { ExaminationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ExaminationId, x => x.Count, ct);

        return exams.Select(e => new ExamDto
        {
            Id = e.Id, AcademicSessionId = e.AcademicSessionId, Name = e.Name, ExamType = e.ExamType,
            StartDate = e.StartDate, EndDate = e.EndDate, Status = e.Status,
            IsRankEnabled = e.IsRankEnabled, SubjectCount = counts.GetValueOrDefault(e.Id)
        }).ToList();
    }

    public async Task<ExamDto> CreateAsync(SaveExamRequest request, CancellationToken ct = default)
    {
        if (request.EndDate < request.StartDate)
            throw DomainException.Validation("Exam end date must be on or after the start date.");

        var session = await _db.AcademicSessions.FirstOrDefaultAsync(s => s.Id == request.AcademicSessionId && s.SchoolId == SchoolId, ct)
            ?? throw DomainException.NotFound("Academic session");

        var exam = new Examination
        {
            SchoolId = SchoolId,
            AcademicSessionId = session.Id,
            Name = request.Name.Trim(),
            ExamType = request.ExamType,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsRankEnabled = request.IsRankEnabled,
            Status = ExamStatus.Draft
        };
        _db.Add(exam);
        await _uow.SaveChangesAsync(ct);

        return new ExamDto
        {
            Id = exam.Id, AcademicSessionId = exam.AcademicSessionId, Name = exam.Name,
            ExamType = exam.ExamType, StartDate = exam.StartDate, EndDate = exam.EndDate,
            Status = exam.Status, IsRankEnabled = exam.IsRankEnabled
        };
    }

    public async Task<ExamDto> UpdateAsync(int examId, UpdateExamRequest request, CancellationToken ct = default)
    {
        if (request.EndDate < request.StartDate)
            throw DomainException.Validation("Exam end date must be on or after the start date.");

        var exam = await GetExamAsync(examId, ct);
        await EnsureEditableAsync(exam, "edited", ct);

        exam.Name = request.Name.Trim();
        exam.ExamType = request.ExamType;
        exam.StartDate = request.StartDate;
        exam.EndDate = request.EndDate;
        exam.IsRankEnabled = request.IsRankEnabled;
        await _uow.SaveChangesAsync(ct);

        var subjectCount = await _db.ExamSubjects.CountAsync(s => s.ExaminationId == exam.Id && !s.IsDeleted, ct);
        return new ExamDto
        {
            Id = exam.Id, AcademicSessionId = exam.AcademicSessionId, Name = exam.Name,
            ExamType = exam.ExamType, StartDate = exam.StartDate, EndDate = exam.EndDate,
            Status = exam.Status, IsRankEnabled = exam.IsRankEnabled, SubjectCount = subjectCount
        };
    }

    public async Task DeleteAsync(int examId, CancellationToken ct = default)
    {
        var exam = await GetExamAsync(examId, ct);
        await EnsureEditableAsync(exam, "deleted", ct);

        var subjects = await _db.ExamSubjects
            .Where(s => s.ExaminationId == exam.Id && s.SchoolId == SchoolId && !s.IsDeleted)
            .ToListAsync(ct);
        foreach (var subject in subjects) subject.IsDeleted = true;
        exam.IsDeleted = true;
        await _uow.SaveChangesAsync(ct);
    }

    private async Task EnsureEditableAsync(Examination exam, string action, CancellationToken ct)
    {
        if (exam.Status != ExamStatus.Draft || exam.PublishedAtUtc is not null)
            throw DomainException.Conflict($"Only a draft exam can be {action}. This exam is {exam.Status}.");

        var hasMarks = await _db.StudentMarks.AnyAsync(m =>
            m.ExaminationId == exam.Id && m.SchoolId == SchoolId && !m.IsDeleted, ct);
        if (hasMarks)
            throw DomainException.Conflict($"Marks have already been entered for this exam, so it cannot be {action}.");
    }

    public async Task<List<ExamSubjectDto>> GetSubjectsAsync(int examId, CancellationToken ct = default)
    {
        var exam = await GetExamAsync(examId, ct);

        return await (
            from es in _db.ExamSubjects
            join subject in _db.Subjects on es.SubjectId equals subject.Id
            join klass in _db.Classes on es.ClassId equals klass.Id
            where es.ExaminationId == exam.Id && !es.IsDeleted
            orderby klass.DisplayOrder, subject.Name
            select new ExamSubjectDto
            {
                Id = es.Id, ClassId = es.ClassId, ClassName = klass.Name,
                SubjectId = es.SubjectId, SubjectName = subject.Name,
                MaxTheoryMarks = es.MaxTheoryMarks, MaxPracticalMarks = es.MaxPracticalMarks,
                PassMarks = es.PassMarks, ExamDate = es.ExamDate
            }).ToListAsync(ct);
    }

    public async Task<ExamSubjectDto> AddSubjectAsync(int examId, SaveExamSubjectRequest request, CancellationToken ct = default)
    {
        var exam = await GetExamAsync(examId, ct);
        if (exam.Status is ExamStatus.Approved or ExamStatus.Published or ExamStatus.Closed)
            throw DomainException.Conflict("Subjects cannot be changed after marks approval.");

        var klass = await _db.Classes.FirstOrDefaultAsync(c => c.Id == request.ClassId && c.SchoolId == SchoolId && !c.IsDeleted, ct)
            ?? throw DomainException.NotFound("Class");
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == request.SubjectId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Subject");

        if (request.MaxTheoryMarks + request.MaxPracticalMarks <= 0)
            throw DomainException.Validation("Maximum marks must be greater than zero.");
        if (request.PassMarks > request.MaxTheoryMarks + request.MaxPracticalMarks)
            throw DomainException.Validation("Pass marks cannot exceed the maximum marks.");

        var duplicate = await _db.ExamSubjects.AnyAsync(es =>
            es.ExaminationId == exam.Id && es.ClassId == request.ClassId &&
            es.SubjectId == request.SubjectId && !es.IsDeleted, ct);
        if (duplicate) throw DomainException.Conflict("This subject is already part of the examination for the class.");

        var entity = new ExamSubject
        {
            SchoolId = SchoolId, ExaminationId = exam.Id, ClassId = klass.Id, SubjectId = subject.Id,
            MaxTheoryMarks = request.MaxTheoryMarks, MaxPracticalMarks = request.MaxPracticalMarks,
            PassMarks = request.PassMarks, ExamDate = request.ExamDate, StartTime = request.StartTime
        };
        _db.Add(entity);
        await _uow.SaveChangesAsync(ct);

        return new ExamSubjectDto
        {
            Id = entity.Id, ClassId = entity.ClassId, ClassName = klass.Name, SubjectId = entity.SubjectId,
            SubjectName = subject.Name, MaxTheoryMarks = entity.MaxTheoryMarks,
            MaxPracticalMarks = entity.MaxPracticalMarks, PassMarks = entity.PassMarks, ExamDate = entity.ExamDate
        };
    }

    public async Task<List<StudentMarkDto>> GetMarksAsync(int examSubjectId, CancellationToken ct = default)
    {
        var examSubject = await _db.ExamSubjects.FirstOrDefaultAsync(es =>
            es.Id == examSubjectId && es.SchoolId == SchoolId && !es.IsDeleted, ct)
            ?? throw DomainException.NotFound("Exam subject");

        var studentsQuery = _db.Students
            .Where(s => s.SchoolId == SchoolId && s.ClassId == examSubject.ClassId &&
                        s.Status == StudentStatus.Active && !s.IsDeleted);

        // Parents and students must never see class-wide marks. Teachers and
        // administrators reach this method through the exams.read permission.
        if (_user.IsInRole(RoleCodes.Parent))
        {
            var guardianId = _user.GuardianId ?? -1;
            studentsQuery = studentsQuery.Where(s => _db.StudentParents.Any(sp =>
                sp.GuardianId == guardianId && sp.StudentId == s.Id && !sp.IsDeleted));
        }
        else if (_user.IsInRole(RoleCodes.Student))
        {
            var studentId = _user.StudentId ?? -1;
            studentsQuery = studentsQuery.Where(s => s.Id == studentId);
        }

        var students = await studentsQuery.OrderBy(s => s.FirstName).ToListAsync(ct);

        var marks = await _db.StudentMarks
            .Where(m => m.ExamSubjectId == examSubjectId && m.SchoolId == SchoolId && !m.IsDeleted)
            .ToDictionaryAsync(m => m.StudentId, ct);

        return students.Select(s =>
        {
            marks.TryGetValue(s.Id, out var mark);
            return new StudentMarkDto
            {
                StudentId = s.Id, StudentName = s.FullName, AdmissionNo = s.AdmissionNo,
                TheoryMarks = mark?.TheoryMarks, PracticalMarks = mark?.PracticalMarks,
                IsAbsent = mark?.IsAbsent ?? false, Remarks = mark?.Remarks
            };
        }).ToList();
    }

    public async Task<int> SaveMarksAsync(int examId, SaveMarksRequest request, CancellationToken ct = default)
    {
        var exam = await GetExamAsync(examId, ct);
        if (exam.Status is ExamStatus.Approved or ExamStatus.Published or ExamStatus.Closed)
            throw DomainException.Conflict("Marks are locked after approval.");

        var examSubject = await _db.ExamSubjects.FirstOrDefaultAsync(es =>
            es.Id == request.ExamSubjectId && es.ExaminationId == exam.Id && es.SchoolId == SchoolId && !es.IsDeleted, ct)
            ?? throw DomainException.NotFound("Exam subject");

        await EnsureSubjectAccessAsync(examSubject, ct);

        if (request.Marks.Select(m => m.StudentId).Distinct().Count() != request.Marks.Count)
            throw DomainException.Validation("Duplicate student entries are not allowed.");

        var studentIds = request.Marks.Select(m => m.StudentId).ToList();
        var validStudents = await _db.Students
            .CountAsync(s => s.SchoolId == SchoolId && s.ClassId == examSubject.ClassId &&
                             studentIds.Contains(s.Id) && !s.IsDeleted, ct);
        if (validStudents != studentIds.Count)
            throw DomainException.Validation("One or more students do not belong to the exam subject's class.");

        var existing = await _db.StudentMarks
            .Where(m => m.ExamSubjectId == examSubject.Id && m.SchoolId == SchoolId && studentIds.Contains(m.StudentId) && !m.IsDeleted)
            .ToDictionaryAsync(m => m.StudentId, ct);

        foreach (var input in request.Marks)
        {
            if (input.TheoryMarks > examSubject.MaxTheoryMarks)
                throw DomainException.Validation($"Theory marks cannot exceed {examSubject.MaxTheoryMarks}.");
            if (input.PracticalMarks > examSubject.MaxPracticalMarks)
                throw DomainException.Validation($"Practical marks cannot exceed {examSubject.MaxPracticalMarks}.");

            if (existing.TryGetValue(input.StudentId, out var mark))
            {
                mark.TheoryMarks = input.IsAbsent ? null : input.TheoryMarks;
                mark.PracticalMarks = input.IsAbsent ? null : input.PracticalMarks;
                mark.IsAbsent = input.IsAbsent;
                mark.Remarks = input.Remarks;
                mark.EnteredByEmployeeId = _user.EmployeeId;
                mark.ModifiedAtUtc = _clock.UtcNow;
            }
            else
            {
                _db.Add(new StudentMark
                {
                    SchoolId = SchoolId, ExaminationId = exam.Id, ExamSubjectId = examSubject.Id,
                    StudentId = input.StudentId,
                    TheoryMarks = input.IsAbsent ? null : input.TheoryMarks,
                    PracticalMarks = input.IsAbsent ? null : input.PracticalMarks,
                    IsAbsent = input.IsAbsent, Remarks = input.Remarks,
                    EnteredByEmployeeId = _user.EmployeeId
                });
            }
        }

        exam.Status = ExamStatus.MarksEntry;
        await _uow.SaveChangesAsync(ct);
        return request.Marks.Count;
    }

    public async Task SubmitForApprovalAsync(int examId, CancellationToken ct = default)
    {
        var exam = await GetExamAsync(examId, ct);
        if (exam.Status != ExamStatus.MarksEntry)
            throw DomainException.Conflict("Only an exam in marks-entry state can be submitted for approval.");

        exam.Status = ExamStatus.PendingApproval;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task ApproveAsync(int examId, CancellationToken ct = default)
    {
        var exam = await GetExamAsync(examId, ct);
        if (exam.Status != ExamStatus.PendingApproval)
            throw DomainException.Conflict("Only an exam pending approval can be approved.");

        exam.Status = ExamStatus.Approved;
        exam.ApprovedAtUtc = _clock.UtcNow;
        exam.ApprovedBy = _user.UserId;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<int> PublishAsync(int examId, CancellationToken ct = default)
    {
        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var exam = await GetExamAsync(examId, token);
            if (exam.Status != ExamStatus.Approved)
                throw DomainException.Conflict("Results can only be published after marks approval.");

            var subjects = await _db.ExamSubjects
                .Where(es => es.ExaminationId == exam.Id && es.SchoolId == SchoolId && !es.IsDeleted)
                .ToListAsync(token);
            if (subjects.Count == 0) throw DomainException.Validation("The examination has no subjects.");

            var classIds = subjects.Select(s => s.ClassId).Distinct().ToList();
            var students = await _db.Students
                .Where(s => s.SchoolId == SchoolId && classIds.Contains(s.ClassId) &&
                            s.Status == StudentStatus.Active && !s.IsDeleted)
                .ToListAsync(token);

            var marks = await _db.StudentMarks
                .Where(m => m.ExaminationId == exam.Id && m.SchoolId == SchoolId && !m.IsDeleted)
                .ToListAsync(token);

            var marksLookup = marks.GroupBy(m => (m.ExamSubjectId, m.StudentId))
                .ToDictionary(g => g.Key, g => g.First());

            var existingCards = await _db.ReportCards
                .Where(r => r.ExaminationId == exam.Id && r.SchoolId == SchoolId && !r.IsDeleted)
                .ToDictionaryAsync(r => r.StudentId, token);

            var cards = new List<ReportCard>();
            var cardsByStudent = new Dictionary<int, ReportCard>();

            foreach (var student in students)
            {
                var studentSubjects = subjects.Where(s => s.ClassId == student.ClassId).ToList();
                if (studentSubjects.Count == 0) continue;

                decimal total = 0, obtained = 0;
                var failed = false;

                foreach (var subject in studentSubjects)
                {
                    var max = subject.MaxTheoryMarks + subject.MaxPracticalMarks;
                    total += max;

                    if (!marksLookup.TryGetValue((subject.Id, student.Id), out var mark) || mark.IsAbsent)
                    {
                        failed = true;
                        continue;
                    }

                    var score = (mark.TheoryMarks ?? 0) + (mark.PracticalMarks ?? 0);
                    obtained += score;
                    if (score < subject.PassMarks) failed = true;
                }

                var percentage = total == 0 ? 0 : Math.Round(obtained / total * 100m, 2);
                var card = existingCards.GetValueOrDefault(student.Id) ?? new ReportCard
                {
                    SchoolId = SchoolId,
                    ExaminationId = exam.Id,
                    StudentId = student.Id
                };

                card.ClassId = student.ClassId;
                card.SectionId = student.SectionId;
                card.TotalMarks = total;
                card.ObtainedMarks = obtained;
                card.Percentage = percentage;
                card.Grade = GradingCalculator.Grade(percentage);
                card.ResultStatus = failed ? "Fail" : "Pass";
                card.PublishedAtUtc = _clock.UtcNow;

                if (existingCards.ContainsKey(student.Id) is false)
                {
                    _db.Add(card);
                    existingCards[student.Id] = card;
                }

                cards.Add(card);
                cardsByStudent[student.Id] = card;
            }

            if (exam.IsRankEnabled)
            {
                foreach (var group in cards.GroupBy(c => c.ClassId))
                {
                    var rank = 1;
                    foreach (var card in group.OrderByDescending(c => c.Percentage).ThenBy(c => c.StudentId))
                        card.Rank = rank++;
                }
            }
            else
            {
                foreach (var card in cards) card.Rank = null;
            }

            exam.Status = ExamStatus.Published;
            exam.PublishedAtUtc = _clock.UtcNow;

            var studentIds = cards.Select(c => c.StudentId).ToList();
            var guardians = await (
                from sp in _db.StudentParents
                join g in _db.Guardians on sp.GuardianId equals g.Id
                where studentIds.Contains(sp.StudentId) && sp.IsPrimary && !sp.IsDeleted
                select new { sp.StudentId, Guardian = g }).ToListAsync(token);

            foreach (var item in guardians)
            {
                _db.Add(new Notification
                {
                    SchoolId = SchoolId,
                    StudentId = item.StudentId,
                    GuardianId = item.Guardian.Id,
                    UserAccountId = item.Guardian.UserAccountId,
                    Title = "Results published",
                    Message = $"Results for {exam.Name} have been published. View the report card in the parent portal.",
                    Channel = NotificationChannel.InApp,
                    Status = NotificationStatus.Pending,
                    Category = "Examination",
                    Recipient = item.Guardian.Phone
                });
            }

            await _uow.SaveChangesAsync(token);
            return cards.Count;
        }, ct);
    }

    public async Task<List<ExamResultRowDto>> GetResultsAsync(int examId, int? classId, CancellationToken ct = default)
    {
        var query =
            from r in _db.ReportCards
            join s in _db.Students on r.StudentId equals s.Id
            where r.ExaminationId == examId && r.SchoolId == SchoolId && !r.IsDeleted
            select new { r, s };

        if (classId is > 0) query = query.Where(x => x.r.ClassId == classId);

        // Parents and students must never see class-wide results. Teachers and
        // administrators reach this method through the exams.read permission.
        if (_user.IsInRole(RoleCodes.Parent))
        {
            var guardianId = _user.GuardianId ?? -1;
            query = query.Where(x => _db.StudentParents.Any(sp =>
                sp.GuardianId == guardianId && sp.StudentId == x.s.Id && !sp.IsDeleted));
        }
        else if (_user.IsInRole(RoleCodes.Student))
        {
            var studentId = _user.StudentId ?? -1;
            query = query.Where(x => x.s.Id == studentId);
        }

        return await query.OrderBy(x => x.r.Rank ?? int.MaxValue).ThenBy(x => x.s.FirstName)
            .Select(x => new ExamResultRowDto
            {
                StudentId = x.s.Id, StudentName = x.s.FirstName + " " + x.s.LastName,
                AdmissionNo = x.s.AdmissionNo, TotalMarks = x.r.TotalMarks,
                ObtainedMarks = x.r.ObtainedMarks, Percentage = x.r.Percentage,
                Grade = x.r.Grade, ResultStatus = x.r.ResultStatus, Rank = x.r.Rank
            }).ToListAsync(ct);
    }

    public async Task<ReportCardDto> GetReportCardAsync(int examId, int studentId, CancellationToken ct = default)
    {
        await EnsureResultAccessAsync(studentId, ct);

        var exam = await GetExamAsync(examId, ct);
        var card = await _db.ReportCards.FirstOrDefaultAsync(r =>
            r.ExaminationId == examId && r.StudentId == studentId && r.SchoolId == SchoolId && !r.IsDeleted, ct)
            ?? throw DomainException.NotFound("Report card");

        var student = await _db.Students.FirstAsync(s => s.Id == studentId, ct);
        var className = await _db.Classes.Where(c => c.Id == student.ClassId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var sectionName = await _db.Sections.Where(s => s.Id == student.SectionId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var school = await _db.Schools.FirstAsync(s => s.Id == SchoolId, ct);

        var subjects = await (
            from es in _db.ExamSubjects
            join subject in _db.Subjects on es.SubjectId equals subject.Id
            where es.ExaminationId == examId && es.ClassId == student.ClassId && !es.IsDeleted
            orderby subject.Name
            select new { es, subject.Name }).ToListAsync(ct);

        var marks = await _db.StudentMarks
            .Where(m => m.ExaminationId == examId && m.StudentId == studentId && !m.IsDeleted)
            .ToDictionaryAsync(m => m.ExamSubjectId, ct);

        var subjectRows = subjects.Select(x =>
        {
            marks.TryGetValue(x.es.Id, out var mark);
            var max = x.es.MaxTheoryMarks + x.es.MaxPracticalMarks;
            var obtained = mark is null || mark.IsAbsent ? 0 : (mark.TheoryMarks ?? 0) + (mark.PracticalMarks ?? 0);
            var pct = max == 0 ? 0 : obtained / max * 100m;

            return new SubjectMarkDto
            {
                SubjectName = x.Name, MaxMarks = max, TheoryMarks = mark?.TheoryMarks,
                PracticalMarks = mark?.PracticalMarks, ObtainedMarks = obtained,
                IsAbsent = mark?.IsAbsent ?? false, Grade = GradingCalculator.Grade(pct)
            };
        }).ToList();

        var dto = new ReportCardDto
        {
            StudentId = student.Id, StudentName = student.FullName, AdmissionNo = student.AdmissionNo,
            ClassName = className, SectionName = sectionName, ExaminationName = exam.Name,
            TotalMarks = card.TotalMarks, ObtainedMarks = card.ObtainedMarks, Percentage = card.Percentage,
            Grade = card.Grade, Rank = card.Rank, ResultStatus = card.ResultStatus, Subjects = subjectRows
        };

        return dto with { Html = BuildReportCardHtml(dto, school) };
    }

    private async Task<Examination> GetExamAsync(int examId, CancellationToken ct) =>
        await _db.Examinations.FirstOrDefaultAsync(e => e.Id == examId && e.SchoolId == SchoolId && !e.IsDeleted, ct)
        ?? throw DomainException.NotFound("Examination");

    private async Task EnsureSubjectAccessAsync(ExamSubject subject, CancellationToken ct)
    {
        if (IsAdmin()) return;

        var employeeId = _user.EmployeeId ?? throw DomainException.Forbidden("Only assigned teachers can enter marks.");

        var assigned = await _db.TeacherAssignments.AnyAsync(t =>
            t.SchoolId == SchoolId && t.EmployeeId == employeeId && t.ClassId == subject.ClassId &&
            (t.SubjectId == null || t.SubjectId == subject.SubjectId) && !t.IsDeleted, ct)
            || await _db.ClassSubjects.AnyAsync(cs =>
                cs.SchoolId == SchoolId && cs.ClassId == subject.ClassId && cs.SubjectId == subject.SubjectId &&
                cs.TeacherId == employeeId && !cs.IsDeleted, ct);

        if (!assigned) throw DomainException.Forbidden("You are not assigned to this class and subject.");
    }

    private async Task EnsureResultAccessAsync(int studentId, CancellationToken ct)
    {
        if (_user.IsInRole(RoleCodes.Parent))
        {
            var guardianId = _user.GuardianId;
            var linked = guardianId is not null && await _db.StudentParents.AnyAsync(sp =>
                sp.GuardianId == guardianId && sp.StudentId == studentId && !sp.IsDeleted, ct);
            if (!linked) throw DomainException.NotFound("Student");
            return;
        }

        if (_user.IsInRole(RoleCodes.Student))
        {
            if (_user.StudentId != studentId) throw DomainException.NotFound("Student");
            return;
        }

        if (!_user.HasPermission(Permissions.ExamsRead)) throw DomainException.Forbidden();
    }

    private bool IsAdmin() =>
        _user.Roles.Any(r => AdminRoles.Contains(r)) || _user.HasPermission(Permissions.SettingsManage);

    private static string BuildReportCardHtml(ReportCardDto dto, School school)
    {
        const string style = "body{font-family:Arial,sans-serif;margin:40px;color:#111827}" +
            "h1{color:#0f766e;margin:0}.head{text-align:center;border-bottom:2px solid #0f766e;padding-bottom:12px}" +
            "table{width:100%;border-collapse:collapse;margin-top:20px}th,td{border:1px solid #d1d5db;padding:8px;text-align:left}" +
            "th{background:#f0fdfa}.summary{margin-top:16px;padding:12px;background:#f9fafb;border:1px solid #e5e7eb}" +
            ".sign{margin-top:48px;text-align:right}";

        var rows = string.Concat(dto.Subjects.Select(s =>
            "<tr><td>" + WebUtility.HtmlEncode(s.SubjectName) + "</td><td>" + s.MaxMarks.ToString("0.##") +
            "</td><td>" + (s.TheoryMarks?.ToString("0.##") ?? "-") + "</td><td>" +
            (s.PracticalMarks?.ToString("0.##") ?? "-") + "</td><td>" + s.ObtainedMarks.ToString("0.##") +
            "</td><td>" + WebUtility.HtmlEncode(s.Grade ?? "-") + "</td></tr>"));

        return "<!doctype html><html><head><meta charset=\"utf-8\"><title>Report card</title><style>" + style +
               "</style></head><body><div class=\"head\"><h1>" + WebUtility.HtmlEncode(school.Name) +
               "</h1><p>Progress Report</p></div>" +
               "<p><strong>" + WebUtility.HtmlEncode(dto.StudentName) + "</strong> (" +
               WebUtility.HtmlEncode(dto.AdmissionNo) + ")<br/>" +
               WebUtility.HtmlEncode(dto.ClassName) + " - " + WebUtility.HtmlEncode(dto.SectionName) + "<br/>" +
               WebUtility.HtmlEncode(dto.ExaminationName) + "</p>" +
               "<table><thead><tr><th>Subject</th><th>Max</th><th>Theory</th><th>Practical</th><th>Obtained</th><th>Grade</th></tr></thead><tbody>" +
               rows + "</tbody></table>" +
               "<div class=\"summary\"><strong>Total:</strong> " + dto.ObtainedMarks.ToString("0.##") + " / " +
               dto.TotalMarks.ToString("0.##") + " &nbsp; | &nbsp; <strong>Percentage:</strong> " +
               dto.Percentage.ToString("0.##") + "% &nbsp; | &nbsp; <strong>Grade:</strong> " +
               WebUtility.HtmlEncode(dto.Grade ?? "-") + " &nbsp; | &nbsp; <strong>Result:</strong> " +
               WebUtility.HtmlEncode(dto.ResultStatus) +
               (dto.Rank is null ? string.Empty : " &nbsp; | &nbsp; <strong>Rank:</strong> " + dto.Rank) +
               "</div><div class=\"sign\"><p>__________________________</p><p>Principal</p></div></body></html>";
    }
}
