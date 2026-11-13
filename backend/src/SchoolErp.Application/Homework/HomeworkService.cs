using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;

namespace SchoolErp.Application.Homework;

public sealed class HomeworkService
{
    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IDateTimeProvider _clock;

    public HomeworkService(IAppDbContext db, IUnitOfWork uow, ICurrentUser user, IDateTimeProvider clock)
    {
        _db = db;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    public async Task<List<HomeworkDto>> ListAsync(int? classId, int? sectionId, int? subjectId,
        CancellationToken ct = default)
    {
        var query =
            from h in _db.Homework
            join subject in _db.Subjects on h.SubjectId equals subject.Id
            join teacher in _db.Employees on h.EmployeeId equals teacher.Id
            where h.SchoolId == SchoolId && !h.IsDeleted && h.IsPublished
            select new { h, SubjectName = subject.Name, TeacherName = teacher.FirstName + " " + teacher.LastName };

        if (classId is > 0) query = query.Where(x => x.h.ClassId == classId);
        if (sectionId is > 0) query = query.Where(x => x.h.SectionId == sectionId);
        if (subjectId is > 0) query = query.Where(x => x.h.SubjectId == subjectId);

        if (_user.IsInRole(RoleCodes.Student) || _user.IsInRole(RoleCodes.Parent))
        {
            var ownStudentId = _user.StudentId ?? -1;
            var guardianId = _user.GuardianId ?? -1;
            var isStudent = _user.IsInRole(RoleCodes.Student);
            var placements = _db.Students.Where(st => st.SchoolId == SchoolId && !st.IsDeleted &&
                (isStudent
                    ? st.Id == ownStudentId
                    : _db.StudentParents.Any(sp => sp.GuardianId == guardianId && sp.StudentId == st.Id && !sp.IsDeleted)));
            query = query.Where(x => placements.Any(st => st.ClassId == x.h.ClassId && st.SectionId == x.h.SectionId));
        }

        var rows = await query.OrderByDescending(x => x.h.DueAtUtc).Take(200).ToListAsync(ct);
        var ids = rows.Select(r => r.h.Id).ToList();
        var counts = await _db.AssignmentSubmissions
            .Where(s => ids.Contains(s.HomeworkId) && !s.IsDeleted)
            .GroupBy(s => s.HomeworkId)
            .Select(g => new { HomeworkId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.HomeworkId, x => x.Count, ct);

        return rows.Select(x => new HomeworkDto
        {
            Id = x.h.Id, ClassId = x.h.ClassId, SectionId = x.h.SectionId,
            SubjectName = x.SubjectName, TeacherName = x.TeacherName, Title = x.h.Title,
            Description = x.h.Description, AttachmentUrl = x.h.AttachmentUrl, DueAtUtc = x.h.DueAtUtc,
            MaxMarks = x.h.MaxMarks, SubmissionCount = counts.GetValueOrDefault(x.h.Id),
            IsOverdue = x.h.DueAtUtc < _clock.UtcNow
        }).ToList();
    }

    public async Task<HomeworkDto> CreateAsync(SaveHomeworkRequest request, CancellationToken ct = default)
    {
        var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == request.SectionId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Section");
        if (section.ClassId != request.ClassId)
            throw DomainException.Validation("The section does not belong to the class.");

        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == request.SubjectId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Subject");

        var employeeId = await ResolveTeacherAsync(request.ClassId, request.SectionId, request.SubjectId, ct);

        var homework = new SchoolErp.Domain.Entities.Homework
        {
            SchoolId = SchoolId,
            BranchId = _user.BranchId,
            ClassId = request.ClassId,
            SectionId = request.SectionId,
            SubjectId = request.SubjectId,
            EmployeeId = employeeId,
            Title = request.Title.Trim(),
            Description = request.Description,
            AttachmentUrl = request.AttachmentUrl,
            DueAtUtc = request.DueAtUtc,
            MaxMarks = request.MaxMarks,
            IsPublished = request.IsPublished
        };
        _db.Add(homework);
        await _uow.SaveChangesAsync(ct);

        return new HomeworkDto
        {
            Id = homework.Id, ClassId = homework.ClassId, SectionId = homework.SectionId,
            SubjectName = subject.Name, TeacherName = string.Empty, Title = homework.Title,
            Description = homework.Description, AttachmentUrl = homework.AttachmentUrl,
            DueAtUtc = homework.DueAtUtc, MaxMarks = homework.MaxMarks
        };
    }

    public async Task<List<SubmissionDto>> GetSubmissionsAsync(int homeworkId, CancellationToken ct = default)
    {
        var homework = await _db.Homework.FirstOrDefaultAsync(h => h.Id == homeworkId && h.SchoolId == SchoolId && !h.IsDeleted, ct)
            ?? throw DomainException.NotFound("Homework");

        return await (
            from submission in _db.AssignmentSubmissions
            join student in _db.Students on submission.StudentId equals student.Id
            where submission.HomeworkId == homework.Id && !submission.IsDeleted
            orderby submission.SubmittedAtUtc
            select new SubmissionDto
            {
                Id = submission.Id, HomeworkId = submission.HomeworkId, StudentId = submission.StudentId,
                StudentName = student.FirstName + " " + student.LastName,
                SubmittedAtUtc = submission.SubmittedAtUtc, FileUrl = submission.FileUrl,
                Remarks = submission.Remarks, Marks = submission.Marks, Feedback = submission.Feedback,
                IsLate = submission.IsLate
            }).ToListAsync(ct);
    }

    public async Task<SubmissionDto> SubmitAsync(int homeworkId, SubmitHomeworkRequest request, CancellationToken ct = default)
    {
        if (_user.StudentId is null) throw DomainException.Forbidden("Only a student can submit homework.");

        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == _user.StudentId && s.SchoolId == SchoolId, ct)
            ?? throw DomainException.NotFound("Student");

        var homework = await _db.Homework.FirstOrDefaultAsync(h => h.Id == homeworkId && h.SchoolId == SchoolId && !h.IsDeleted, ct)
            ?? throw DomainException.NotFound("Homework");

        if (homework.ClassId != student.ClassId || homework.SectionId != student.SectionId)
            throw DomainException.Forbidden("This homework is not assigned to your class.");

        var submission = await _db.AssignmentSubmissions
            .FirstOrDefaultAsync(s => s.HomeworkId == homeworkId && s.StudentId == student.Id && !s.IsDeleted, ct);

        if (submission is null)
        {
            submission = new AssignmentSubmission
            {
                SchoolId = SchoolId,
                HomeworkId = homeworkId,
                StudentId = student.Id
            };
            _db.Add(submission);
        }

        submission.SubmittedAtUtc = _clock.UtcNow;
        submission.FileUrl = request.FileUrl;
        submission.Remarks = request.Remarks;
        submission.IsLate = _clock.UtcNow > homework.DueAtUtc;

        await _uow.SaveChangesAsync(ct);

        return new SubmissionDto
        {
            Id = submission.Id, HomeworkId = submission.HomeworkId, StudentId = submission.StudentId,
            StudentName = student.FullName, SubmittedAtUtc = submission.SubmittedAtUtc,
            FileUrl = submission.FileUrl, Remarks = submission.Remarks,
            Marks = submission.Marks, Feedback = submission.Feedback, IsLate = submission.IsLate
        };
    }

    public async Task<SubmissionDto> GradeAsync(int submissionId, GradeSubmissionRequest request, CancellationToken ct = default)
    {
        var submission = await _db.AssignmentSubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Submission");

        var homework = await _db.Homework.FirstAsync(h => h.Id == submission.HomeworkId, ct);
        if (homework.MaxMarks is not null && request.Marks > homework.MaxMarks)
            throw DomainException.Validation($"Marks cannot exceed {homework.MaxMarks}.");

        submission.Marks = request.Marks;
        submission.Feedback = request.Feedback;
        await _uow.SaveChangesAsync(ct);

        var student = await _db.Students.FirstAsync(s => s.Id == submission.StudentId, ct);

        return new SubmissionDto
        {
            Id = submission.Id, HomeworkId = submission.HomeworkId, StudentId = submission.StudentId,
            StudentName = student.FullName, SubmittedAtUtc = submission.SubmittedAtUtc,
            FileUrl = submission.FileUrl, Remarks = submission.Remarks,
            Marks = submission.Marks, Feedback = submission.Feedback, IsLate = submission.IsLate
        };
    }

    private async Task<int> ResolveTeacherAsync(int classId, int sectionId, int subjectId, CancellationToken ct)
    {
        if (_user.EmployeeId is not null)
        {
            var assigned = await _db.TeacherAssignments.AnyAsync(t =>
                t.SchoolId == SchoolId && t.EmployeeId == _user.EmployeeId && t.ClassId == classId &&
                t.SectionId == sectionId && !t.IsDeleted, ct);

            if (assigned || _user.IsInRole(RoleCodes.Principal) || _user.IsInRole(RoleCodes.Administrator) ||
                _user.IsInRole(RoleCodes.SuperAdmin))
                return _user.EmployeeId.Value;

            if (_user.IsInRole(RoleCodes.Teacher) || _user.IsInRole(RoleCodes.ClassCoordinator))
                throw DomainException.Forbidden("You are not assigned to this class and section. Ask the principal to assign you under Class & teachers.");
        }

        var sectionTeacher = await _db.Sections.Where(s => s.Id == sectionId && s.ClassTeacherId != null)
            .Select(s => s.ClassTeacherId).FirstOrDefaultAsync(ct);
        if (sectionTeacher is not null) return sectionTeacher.Value;

        var subjectTeacher = await _db.ClassSubjects
            .Where(cs => cs.ClassId == classId && cs.SubjectId == subjectId && cs.TeacherId != null && !cs.IsDeleted)
            .Select(cs => cs.TeacherId).FirstOrDefaultAsync(ct);

        return subjectTeacher ?? throw DomainException.Validation("No teacher is assigned to this class and subject.");
    }
}
