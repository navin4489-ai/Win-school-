using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Dashboard;

public sealed class DashboardService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTimeProvider _clock;

    public DashboardService(IAppDbContext db, ICurrentUser user, IDateTimeProvider clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    public async Task<DashboardDto> BuildAsync(CancellationToken ct = default)
    {
        var today = _clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthStartUtc = monthStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var trendStart = monthStart.AddMonths(-5);
        var trendStartUtc = trendStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var weekStart = today.AddDays(-6);

        var role = _user.Roles.FirstOrDefault() ?? string.Empty;

        var studentCount = await _db.Students.CountAsync(s =>
            s.SchoolId == SchoolId && s.Status == StudentStatus.Active && !s.IsDeleted, ct);

        var staffCount = await _db.Employees.CountAsync(e =>
            e.SchoolId == SchoolId && e.Status == EmploymentStatus.Active && !e.IsDeleted, ct);

        var branchCount = await _db.Branches.CountAsync(b => b.SchoolId == SchoolId && !b.IsDeleted, ct);

        var paymentsThisMonth = await _db.Payments
            .Where(p => p.SchoolId == SchoolId && !p.IsDeleted && p.Status != PaymentStatus.Failed &&
                        p.PaidAtUtc >= monthStartUtc)
            .SumAsync(p => (decimal?)(p.Amount - p.RefundedAmount), ct) ?? 0;

        var paymentsToday = await _db.Payments
            .Where(p => p.SchoolId == SchoolId && !p.IsDeleted && p.Status != PaymentStatus.Failed &&
                        p.PaidAtUtc >= today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
            .SumAsync(p => (decimal?)(p.Amount - p.RefundedAmount), ct) ?? 0;

        var outstanding = await (
            from i in _db.FeeInstallments
            join a in _db.StudentFeeAssignments on i.StudentFeeAssignmentId equals a.Id
            where i.SchoolId == SchoolId && !i.IsDeleted && !a.IsDeleted && a.IsActive &&
                  i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived &&
                  i.Amount > i.PaidAmount
            select i.Amount - i.PaidAmount).SumAsync(ct);

        var todayRecords = await _db.StudentAttendances
            .Where(a => a.SchoolId == SchoolId && a.AttendanceDate == today && !a.IsDeleted)
            .Select(a => a.Status).ToListAsync(ct);

        var attendanceTotal = todayRecords.Count;
        var attended = todayRecords.Count(s => s is AttendanceStatus.Present or AttendanceStatus.Late) +
                       todayRecords.Count(s => s == AttendanceStatus.HalfDay) * 0.5m;
        var attendancePercent = attendanceTotal == 0 ? 0 : Math.Round(attended / attendanceTotal * 100m, 1);

        var admissionsThisMonth = await _db.Admissions.CountAsync(a =>
            a.SchoolId == SchoolId && !a.IsDeleted && a.AppliedAtUtc >= monthStartUtc, ct);

        var pendingAdmissions = await _db.Admissions.CountAsync(a =>
            a.SchoolId == SchoolId && !a.IsDeleted &&
            a.Status != AdmissionStatus.Enrolled && a.Status != AdmissionStatus.Rejected &&
            a.Status != AdmissionStatus.Cancelled, ct);

        var paymentsForTrend = await _db.Payments
            .Where(p => p.SchoolId == SchoolId && !p.IsDeleted && p.Status != PaymentStatus.Failed &&
                        p.PaidAtUtc >= trendStartUtc)
            .Select(p => new { p.PaidAtUtc, p.Amount, p.RefundedAmount })
            .ToListAsync(ct);

        var feeTrend = paymentsForTrend
            .GroupBy(p => new { p.PaidAtUtc.Year, p.PaidAtUtc.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new ChartPointDto
            {
                Label = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM"),
                Value = g.Sum(x => x.Amount - x.RefundedAmount)
            }).ToList();

        var admissionsForTrend = await _db.Admissions
            .Where(a => a.SchoolId == SchoolId && !a.IsDeleted && a.AppliedAtUtc >= trendStartUtc)
            .Select(a => a.AppliedAtUtc).ToListAsync(ct);

        var admissionTrend = admissionsForTrend
            .GroupBy(d => new { d.Year, d.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new ChartPointDto
            {
                Label = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM"),
                Value = g.Count()
            }).ToList();

        var weekRecords = await _db.StudentAttendances
            .Where(a => a.SchoolId == SchoolId && a.AttendanceDate >= weekStart && a.AttendanceDate <= today && !a.IsDeleted)
            .Select(a => new { a.AttendanceDate, a.Status }).ToListAsync(ct);

        var attendanceTrend = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = weekStart.AddDays(offset);
            var day = weekRecords.Where(r => r.AttendanceDate == date).ToList();
            var present = day.Count(r => r.Status is AttendanceStatus.Present or AttendanceStatus.Late) +
                          day.Count(r => r.Status == AttendanceStatus.HalfDay) * 0.5m;
            return new ChartPointDto
            {
                Label = date.ToString("ddd"),
                Value = day.Count == 0 ? 0 : Math.Round(present / day.Count * 100m, 1)
            };
        }).ToList();

        var cards = new List<DashboardCardDto>();

        if (role == RoleCodes.Teacher)
        {
            var employeeId = _user.EmployeeId ?? 0;
            var assignments = await _db.TeacherAssignments
                .Where(t => t.SchoolId == SchoolId && t.EmployeeId == employeeId && !t.IsDeleted)
                .Select(t => new { t.ClassId, t.SectionId }).Distinct().CountAsync(ct);

            var todayPeriods = await _db.TimetableEntries.CountAsync(t =>
                t.SchoolId == SchoolId && t.EmployeeId == employeeId && t.DayOfWeek == (int)today.DayOfWeek && !t.IsDeleted, ct);

            var pendingGrading = await (
                from s in _db.AssignmentSubmissions
                join h in _db.Homework on s.HomeworkId equals h.Id
                where h.EmployeeId == employeeId && s.SchoolId == SchoolId && s.Marks == null && !s.IsDeleted
                select s.Id).CountAsync(ct);

            var markedToday = await _db.StudentAttendances.AnyAsync(a =>
                a.SchoolId == SchoolId && a.MarkedByEmployeeId == employeeId && a.AttendanceDate == today && !a.IsDeleted, ct);

            cards.Add(new DashboardCardDto { Key = "classes", Label = "My classes", Value = assignments.ToString(), Hint = "Assigned class-sections" });
            cards.Add(new DashboardCardDto { Key = "periods", Label = "Periods today", Value = todayPeriods.ToString(), Hint = "Scheduled periods" });
            cards.Add(new DashboardCardDto { Key = "grading", Label = "Pending grading", Value = pendingGrading.ToString(), Tone = pendingGrading > 0 ? "warning" : "positive" });
            cards.Add(new DashboardCardDto { Key = "attendance", Label = "Attendance today", Value = markedToday ? "Marked" : "Pending", Tone = markedToday ? "positive" : "warning" });
        }
        else if (role == RoleCodes.Accountant)
        {
            var refundsPending = await _db.Refunds.CountAsync(r =>
                r.SchoolId == SchoolId && r.Status == RefundStatus.Pending && !r.IsDeleted);
            var failedPayments = await _db.PaymentTransactions.CountAsync(t =>
                t.SchoolId == SchoolId && t.Status == PaymentTransactionStatus.Failed && !t.IsDeleted);

            cards.Add(new DashboardCardDto { Key = "today", Label = "Collected today", Value = paymentsToday.ToString("N0"), Unit = "INR", Tone = "positive" });
            cards.Add(new DashboardCardDto { Key = "month", Label = "Collected this month", Value = paymentsThisMonth.ToString("N0"), Unit = "INR" });
            cards.Add(new DashboardCardDto { Key = "outstanding", Label = "Outstanding fees", Value = outstanding.ToString("N0"), Unit = "INR", Tone = outstanding > 0 ? "warning" : "positive" });
            cards.Add(new DashboardCardDto { Key = "refunds", Label = "Refunds pending", Value = refundsPending.ToString(), Tone = refundsPending > 0 ? "warning" : "neutral", Hint = $"{failedPayments} failed attempts" });
        }
        else if (role == RoleCodes.Parent)
        {
            var guardianId = _user.GuardianId;
            var children = guardianId is null ? 0 : await _db.StudentParents
                .CountAsync(sp => sp.SchoolId == SchoolId && sp.GuardianId == guardianId && !sp.IsDeleted);

            cards.Add(new DashboardCardDto { Key = "children", Label = "Children", Value = children.ToString() });
            cards.Add(new DashboardCardDto { Key = "due", Label = "Fee balance", Value = outstanding.ToString("N0"), Unit = "INR", Tone = outstanding > 0 ? "warning" : "positive" });
            cards.Add(new DashboardCardDto { Key = "attendance", Label = "Attendance today", Value = $"{attendancePercent:0.#}%", Hint = "School-wide view" });
        }
        else
        {
            cards.Add(new DashboardCardDto { Key = "students", Label = "Active students", Value = studentCount.ToString("N0"), Hint = "Across all branches" });
            cards.Add(new DashboardCardDto { Key = "staff", Label = "Active staff", Value = staffCount.ToString("N0"), Hint = $"{branchCount} branch(es)" });
            cards.Add(new DashboardCardDto
            {
                Key = "collected", Label = "Fees collected", Value = paymentsThisMonth.ToString("N0"), Unit = "INR",
                Tone = "positive", Hint = $"{admissionsThisMonth} admissions this month"
            });
            cards.Add(new DashboardCardDto
            {
                Key = "outstanding", Label = "Outstanding", Value = outstanding.ToString("N0"), Unit = "INR",
                Tone = outstanding > 0 ? "warning" : "positive", Hint = $"{pendingAdmissions} pending applications"
            });
            cards.Add(new DashboardCardDto { Key = "attendance", Label = "Attendance today", Value = $"{attendancePercent:0.#}%", Hint = attendanceTotal == 0 ? "Not marked yet" : $"{attendanceTotal} records" });
        }

        var activities = await _db.AuditLogs
            .Where(a => a.SchoolId == SchoolId)
            .OrderByDescending(a => a.Id)
            .Take(6)
            .Select(a => new ActivityDto
            {
                Title = a.Action + " · " + a.EntityName,
                Detail = a.UserEmail,
                AtUtc = a.CreatedAtUtc,
                Tone = a.Action == "LoginFailed" ? "danger" : "neutral"
            }).ToListAsync(ct);

        var timetable = new List<TimetableSlotDto>();
        if (_user.EmployeeId is not null)
        {
            timetable = await (
                from t in _db.TimetableEntries
                join subject in _db.Subjects on t.SubjectId equals subject.Id
                join klass in _db.Classes on t.ClassId equals klass.Id
                where t.SchoolId == SchoolId && t.EmployeeId == _user.EmployeeId &&
                      t.DayOfWeek == (int)today.DayOfWeek && !t.IsDeleted
                orderby t.PeriodNo
                select new TimetableSlotDto
                {
                    Period = "P" + t.PeriodNo, Subject = subject.Name, Class = klass.Name,
                    Room = t.Room, StartTime = t.StartTime, EndTime = t.EndTime
                }).ToListAsync(ct);
        }

        return new DashboardDto
        {
            Role = role,
            AcademicSession = await _db.AcademicSessions
                .Where(s => s.SchoolId == SchoolId && s.IsCurrent && !s.IsDeleted)
                .Select(s => s.Name).FirstOrDefaultAsync(ct),
            Cards = cards,
            FeeCollectionTrend = feeTrend,
            AttendanceTrend = attendanceTrend,
            AdmissionTrend = admissionTrend,
            RecentActivity = activities,
            TodayTimetable = timetable
        };
    }
}
