using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Parents;

public sealed class ParentService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTimeProvider _clock;

    public ParentService(IAppDbContext db, ICurrentUser user, IDateTimeProvider clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    public async Task<List<ChildDto>> GetChildrenAsync(CancellationToken ct = default)
    {
        var guardianId = await ResolveGuardianIdAsync(ct);
        if (guardianId is null) return new List<ChildDto>();

        return await (
            from sp in _db.StudentParents
            join s in _db.Students on sp.StudentId equals s.Id
            join klass in _db.Classes on s.ClassId equals klass.Id
            join section in _db.Sections on s.SectionId equals section.Id
            where sp.GuardianId == guardianId && s.SchoolId == SchoolId && !s.IsDeleted && !sp.IsDeleted
            orderby s.FirstName
            select new ChildDto
            {
                StudentId = s.Id, AdmissionNo = s.AdmissionNo,
                FullName = s.FirstName + " " + s.LastName, ClassName = klass.Name,
                SectionName = section.Name, PhotoUrl = s.PhotoUrl, RollNo = s.RollNo
            }).ToListAsync(ct);
    }

    public async Task<ChildOverviewDto> GetOverviewAsync(int studentId, CancellationToken ct = default)
    {
        await EnsureLinkedAsync(studentId, ct);
        var child = (await GetChildrenAsync(ct)).FirstOrDefault(c => c.StudentId == studentId)
            ?? throw DomainException.NotFound("Student");

        var from = _clock.Today.AddDays(-30);
        var attendance = await BuildAttendanceAsync(studentId, from, _clock.Today, ct);
        var fees = await BuildFeesAsync(studentId, ct);

        var results = await (
            from r in _db.ReportCards
            join e in _db.Examinations on r.ExaminationId equals e.Id
            where r.StudentId == studentId && r.SchoolId == SchoolId && r.PublishedAtUtc != null && !r.IsDeleted
            orderby r.PublishedAtUtc descending
            select new ResultSummaryDto
            {
                ExaminationId = e.Id, ExaminationName = e.Name, Percentage = r.Percentage,
                Grade = r.Grade, ResultStatus = r.ResultStatus, Rank = r.Rank
            }).Take(5).ToListAsync(ct);

        var submittedIds = await _db.AssignmentSubmissions
            .Where(s => s.StudentId == studentId && !s.IsDeleted)
            .Select(s => s.HomeworkId).ToListAsync(ct);

        var homework = await (
            from h in _db.Homework
            join subject in _db.Subjects on h.SubjectId equals subject.Id
            join s in _db.Students on h.ClassId equals s.ClassId
            where s.Id == studentId && h.SchoolId == SchoolId && h.IsPublished && !h.IsDeleted &&
                  h.SectionId == s.SectionId
            orderby h.DueAtUtc
            select new { h, subject.Name }).Take(20).ToListAsync(ct);

        var pending = homework.Where(x => !submittedIds.Contains(x.h.Id)).Take(5)
            .Select(x => new HomeworkSummaryDto
            {
                Id = x.h.Id, Subject = x.Name, Title = x.h.Title, DueAtUtc = x.h.DueAtUtc, Submitted = false
            }).ToList();

        var announcements = await _db.Announcements
            .Where(a => a.SchoolId == SchoolId && !a.IsDeleted &&
                        (a.ExpiresAtUtc == null || a.ExpiresAtUtc > _clock.UtcNow) &&
                        (a.Audience == "All" || a.Audience == "Parents"))
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(5)
            .Select(a => new AnnouncementDto
            {
                Id = a.Id, Title = a.Title, Body = a.Body, PublishedAtUtc = a.PublishedAtUtc
            }).ToListAsync(ct);

        return new ChildOverviewDto
        {
            Child = child, Attendance = attendance, Fees = fees, RecentResults = results,
            PendingHomework = pending, Announcements = announcements
        };
    }

    public async Task<AttendanceSummaryDto> GetAttendanceAsync(int studentId, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        await EnsureLinkedAsync(studentId, ct);
        return await BuildAttendanceAsync(studentId, from, to, ct);
    }

    public async Task<FeeSummaryDto> GetFeesAsync(int studentId, CancellationToken ct = default)
    {
        await EnsureLinkedAsync(studentId, ct);
        return await BuildFeesAsync(studentId, ct);
    }

    public async Task<List<ResultSummaryDto>> GetResultsAsync(int studentId, CancellationToken ct = default)
    {
        await EnsureLinkedAsync(studentId, ct);

        return await (
            from r in _db.ReportCards
            join e in _db.Examinations on r.ExaminationId equals e.Id
            where r.StudentId == studentId && r.SchoolId == SchoolId && r.PublishedAtUtc != null && !r.IsDeleted
            orderby r.PublishedAtUtc descending
            select new ResultSummaryDto
            {
                ExaminationId = e.Id, ExaminationName = e.Name, Percentage = r.Percentage,
                Grade = r.Grade, ResultStatus = r.ResultStatus, Rank = r.Rank
            }).ToListAsync(ct);
    }

    private async Task<int?> ResolveGuardianIdAsync(CancellationToken ct)
    {
        if (_user.GuardianId is not null) return _user.GuardianId;

        var userId = _user.UserId;
        if (userId is null) return null;

        return await _db.UserAccounts.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => u.GuardianId)
            .FirstOrDefaultAsync(ct);
    }

    private async Task EnsureLinkedAsync(int studentId, CancellationToken ct)
    {
        var guardianId = await ResolveGuardianIdAsync(ct);
        if (guardianId is null) throw DomainException.Forbidden();

        var linked = await _db.StudentParents.AnyAsync(sp =>
            sp.GuardianId == guardianId && sp.StudentId == studentId && !sp.IsDeleted, ct);

        if (!linked) throw DomainException.NotFound("Student");
    }

    private async Task<AttendanceSummaryDto> BuildAttendanceAsync(int studentId, DateOnly from, DateOnly to,
        CancellationToken ct)
    {
        var records = await _db.StudentAttendances
            .Where(a => a.SchoolId == SchoolId && a.StudentId == studentId &&
                        a.AttendanceDate >= from && a.AttendanceDate <= to && !a.IsDeleted)
            .ToListAsync(ct);

        var total = records.Count;
        var presentDays = records.Count(r => r.Status == AttendanceStatus.Present) +
                          records.Count(r => r.Status == AttendanceStatus.Late) +
                          records.Count(r => r.Status == AttendanceStatus.HalfDay) * 0.5m;

        return new AttendanceSummaryDto
        {
            TotalDays = total,
            Present = records.Count(r => r.Status == AttendanceStatus.Present),
            Absent = records.Count(r => r.Status == AttendanceStatus.Absent),
            Late = records.Count(r => r.Status == AttendanceStatus.Late),
            HalfDay = records.Count(r => r.Status == AttendanceStatus.HalfDay),
            Percentage = total == 0 ? 0 : Math.Round(presentDays / total * 100m, 2)
        };
    }

    private async Task<FeeSummaryDto> BuildFeesAsync(int studentId, CancellationToken ct)
    {
        var assignmentIds = await _db.StudentFeeAssignments
            .Where(a => a.StudentId == studentId && a.SchoolId == SchoolId && a.IsActive && !a.IsDeleted)
            .Select(a => a.Id).ToListAsync(ct);

        var installments = await _db.FeeInstallments
            .Where(i => assignmentIds.Contains(i.StudentFeeAssignmentId) && !i.IsDeleted)
            .OrderBy(i => i.DueDate).ToListAsync(ct);

        var paid = await _db.Payments
            .Where(p => p.StudentId == studentId && p.SchoolId == SchoolId && !p.IsDeleted && p.Status != PaymentStatus.Failed)
            .SumAsync(p => (decimal?)(p.Amount - p.RefundedAmount), ct) ?? 0;

        var today = _clock.Today;

        return new FeeSummaryDto
        {
            TotalDue = Math.Round(installments.Sum(i => i.Amount), 2),
            TotalPaid = Math.Round(paid, 2),
            Balance = Math.Round(installments.Sum(i => i.Amount - i.PaidAmount), 2),
            Overdue = Math.Round(installments
                .Where(i => i.DueDate < today && i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived)
                .Sum(i => i.Amount - i.PaidAmount), 2),
            Installments = installments.Select(i => new InstallmentSummaryDto
            {
                Id = i.Id, Name = i.Name, DueDate = i.DueDate, Amount = i.Amount,
                PaidAmount = i.PaidAmount, Balance = i.Amount - i.PaidAmount, Status = i.Status
            }).ToList()
        };
    }
}
