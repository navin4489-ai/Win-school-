using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Attendance;

public sealed class AttendanceService
{
    private static readonly HashSet<string> AdminRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleCodes.SuperAdmin, RoleCodes.SchoolOwner, RoleCodes.Principal, RoleCodes.Administrator
    };

    private static readonly TimeSpan LateAfter = new(9, 15, 0);

    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IDateTimeProvider _clock;

    public AttendanceService(IAppDbContext db, IUnitOfWork uow, ICurrentUser user, IDateTimeProvider clock)
    {
        _db = db;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    public async Task<ClassAttendanceDto> GetClassRosterAsync(int classId, int sectionId, DateOnly date,
        CancellationToken ct = default)
    {
        if (_user.IsInRole(RoleCodes.Parent) || _user.IsInRole(RoleCodes.Student))
            throw DomainException.Forbidden();

        var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == sectionId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Section");
        if (section.ClassId != classId) throw DomainException.Validation("The section does not belong to the class.");

        var className = await _db.Classes.Where(c => c.Id == classId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

        var students = await _db.Students
            .Where(s => s.SchoolId == SchoolId && s.ClassId == classId && s.SectionId == sectionId &&
                        s.Status == StudentStatus.Active && !s.IsDeleted)
            .OrderBy(s => s.RollNo).ThenBy(s => s.FirstName)
            .ToListAsync(ct);

        var records = await _db.StudentAttendances
            .Where(a => a.SchoolId == SchoolId && a.ClassId == classId && a.SectionId == sectionId &&
                        a.AttendanceDate == date && !a.IsDeleted)
            .ToDictionaryAsync(a => a.StudentId, ct);

        return new ClassAttendanceDto
        {
            ClassId = classId, ClassName = className, SectionId = sectionId, SectionName = section.Name,
            Date = date, IsSubmitted = records.Count > 0,
            Students = students.Select(s => new RosterEntryDto
            {
                StudentId = s.Id, AdmissionNo = s.AdmissionNo, FullName = s.FullName, RollNo = s.RollNo,
                Status = records.TryGetValue(s.Id, out var record) ? record.Status : null,
                Remarks = records.TryGetValue(s.Id, out var existing) ? existing.Remarks : null
            }).ToList()
        };
    }

    public async Task<MarkAttendanceResult> MarkBulkAsync(MarkAttendanceRequest request, CancellationToken ct = default)
    {
        if (request.Date > _clock.Today)
            throw DomainException.Validation("Attendance cannot be marked for a future date.");
        if (request.Entries.Count == 0)
            throw DomainException.Validation("At least one student entry is required.");

        var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == request.SectionId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Section");
        if (section.ClassId != request.ClassId)
            throw DomainException.Validation("The section does not belong to the class.");

        await EnsureCanMarkAsync(request.ClassId, request.SectionId, ct);

        var studentIds = request.Entries.Select(e => e.StudentId).Distinct().ToList();
        var validStudents = await _db.Students
            .Where(s => s.SchoolId == SchoolId && s.ClassId == request.ClassId &&
                        s.SectionId == request.SectionId && studentIds.Contains(s.Id) && !s.IsDeleted)
            .ToDictionaryAsync(s => s.Id, s => s, ct);

        var missing = studentIds.Except(validStudents.Keys).ToList();
        if (missing.Count > 0)
            throw DomainException.Validation($"Students not in this class/section: {string.Join(", ", missing)}.");

        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var existing = await _db.StudentAttendances
                .Where(a => a.SchoolId == SchoolId && a.ClassId == request.ClassId && a.SectionId == request.SectionId &&
                            a.AttendanceDate == request.Date && studentIds.Contains(a.StudentId) && !a.IsDeleted)
                .ToDictionaryAsync(a => a.StudentId, token);

            var created = 0;
            var updated = 0;
            var newlyAbsent = new List<int>();

            foreach (var entry in request.Entries)
            {
                if (!Enum.IsDefined(entry.Status))
                    throw DomainException.Validation("Invalid attendance status.");

                if (existing.TryGetValue(entry.StudentId, out var record))
                {
                    if (record.Status != AttendanceStatus.Absent && entry.Status == AttendanceStatus.Absent)
                        newlyAbsent.Add(entry.StudentId);

                    record.Status = entry.Status;
                    record.Remarks = entry.Remarks;
                    record.MarkedAtUtc = _clock.UtcNow;
                    record.MarkedByEmployeeId = _user.EmployeeId;
                    record.Source = AttendanceSource.Manual;
                    updated++;
                }
                else
                {
                    _db.Add(new StudentAttendance
                    {
                        SchoolId = SchoolId,
                        StudentId = entry.StudentId,
                        ClassId = request.ClassId,
                        SectionId = request.SectionId,
                        AttendanceDate = request.Date,
                        Status = entry.Status,
                        Remarks = entry.Remarks,
                        MarkedByEmployeeId = _user.EmployeeId,
                        MarkedAtUtc = _clock.UtcNow,
                        Source = AttendanceSource.Manual
                    });

                    if (entry.Status == AttendanceStatus.Absent) newlyAbsent.Add(entry.StudentId);
                    created++;
                }
            }

            var notifications = 0;
            if (newlyAbsent.Count > 0)
            {
                var guardians = await (
                    from sp in _db.StudentParents
                    join g in _db.Guardians on sp.GuardianId equals g.Id
                    where newlyAbsent.Contains(sp.StudentId) && sp.IsPrimary && !sp.IsDeleted
                    select new { sp.StudentId, Guardian = g }).ToListAsync(token);

                foreach (var item in guardians)
                {
                    var student = validStudents[item.StudentId];
                    _db.Add(new Notification
                    {
                        SchoolId = SchoolId,
                        StudentId = item.StudentId,
                        GuardianId = item.Guardian.Id,
                        UserAccountId = item.Guardian.UserAccountId,
                        Title = "Absence recorded",
                        Message = $"{student.FullName} was marked absent on {request.Date:dd MMM yyyy}.",
                        Channel = NotificationChannel.Sms,
                        Status = NotificationStatus.Pending,
                        Category = "Attendance",
                        Recipient = item.Guardian.Phone
                    });
                    notifications++;
                }
            }

            await _uow.SaveChangesAsync(token);
            return new MarkAttendanceResult { Created = created, Updated = updated, NotificationsQueued = notifications };
        }, ct);
    }

    public async Task<StudentAttendanceSummaryDto> GetStudentSummaryAsync(int studentId, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        if (to < from) throw DomainException.Validation("The end date must be on or after the start date.");
        await EnsureCanReadStudentAsync(studentId, ct);

        var records = await _db.StudentAttendances
            .Where(a => a.SchoolId == SchoolId && a.StudentId == studentId &&
                        a.AttendanceDate >= from && a.AttendanceDate <= to && !a.IsDeleted)
            .OrderBy(a => a.AttendanceDate)
            .ToListAsync(ct);

        var presentDays = records.Count(r => r.Status == AttendanceStatus.Present) +
                          records.Count(r => r.Status == AttendanceStatus.Late) +
                          records.Count(r => r.Status == AttendanceStatus.HalfDay) * 0.5m;

        var total = records.Count;
        var percentage = total == 0 ? 0 : Math.Round(presentDays / total * 100m, 2);

        return new StudentAttendanceSummaryDto
        {
            StudentId = studentId, From = from, To = to, TotalDays = total,
            Present = records.Count(r => r.Status == AttendanceStatus.Present),
            Absent = records.Count(r => r.Status == AttendanceStatus.Absent),
            Late = records.Count(r => r.Status == AttendanceStatus.Late),
            HalfDay = records.Count(r => r.Status == AttendanceStatus.HalfDay),
            Percentage = percentage,
            Records = records.Select(r => new StudentAttendanceRecordDto
            {
                Date = r.AttendanceDate, Status = r.Status, Remarks = r.Remarks
            }).ToList()
        };
    }

    public async Task<List<StaffAttendanceDto>> GetStaffAttendanceAsync(DateOnly date, CancellationToken ct = default)
    {
        var employees = await _db.Employees
            .Where(e => e.SchoolId == SchoolId && e.Status == EmploymentStatus.Active && !e.IsDeleted)
            .OrderBy(e => e.FirstName).ToListAsync(ct);

        var records = await _db.EmployeeAttendances
            .Where(a => a.SchoolId == SchoolId && a.AttendanceDate == date && !a.IsDeleted)
            .ToDictionaryAsync(a => a.EmployeeId, ct);

        return employees.Select(e =>
        {
            records.TryGetValue(e.Id, out var record);
            return new StaffAttendanceDto
            {
                EmployeeId = e.Id, EmployeeName = e.FullName, EmployeeNo = e.EmployeeNo, Date = date,
                CheckInUtc = record?.CheckInUtc, CheckOutUtc = record?.CheckOutUtc,
                Status = record?.Status ?? AttendanceStatus.Absent,
                IsLate = record?.CheckInUtc?.TimeOfDay > LateAfter
            };
        }).ToList();
    }

    public async Task<StaffAttendanceDto> CheckInAsync(CheckInRequest request, CancellationToken ct = default)
    {
        var employeeId = await ResolveEmployeeIdAsync(request.EmployeeId, ct);
        var today = _clock.Today;
        var now = _clock.UtcNow;

        var record = await _db.EmployeeAttendances
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.SchoolId == SchoolId &&
                                      a.AttendanceDate == today && !a.IsDeleted, ct);

        if (record is not null && record.CheckInUtc is not null)
            throw DomainException.Conflict("Already checked in for today.");

        if (record is null)
        {
            record = new EmployeeAttendance
            {
                SchoolId = SchoolId,
                EmployeeId = employeeId,
                AttendanceDate = today
            };
            _db.Add(record);
        }

        record.CheckInUtc = now;
        record.Status = now.TimeOfDay > LateAfter ? AttendanceStatus.Late : AttendanceStatus.Present;
        record.Source = request.Source;

        await _uow.SaveChangesAsync(ct);
        return await MapStaffAsync(record, ct);
    }

    public async Task<StaffAttendanceDto> CheckOutAsync(CheckInRequest request, CancellationToken ct = default)
    {
        var employeeId = await ResolveEmployeeIdAsync(request.EmployeeId, ct);
        var today = _clock.Today;

        var record = await _db.EmployeeAttendances
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.SchoolId == SchoolId &&
                                      a.AttendanceDate == today && !a.IsDeleted, ct)
            ?? throw DomainException.Validation("No check-in exists for today.");

        if (record.CheckInUtc is null) throw DomainException.Validation("Check in before checking out.");
        if (record.CheckOutUtc is not null) throw DomainException.Conflict("Already checked out for today.");

        record.CheckOutUtc = _clock.UtcNow;
        await _uow.SaveChangesAsync(ct);
        return await MapStaffAsync(record, ct);
    }

    private async Task EnsureCanMarkAsync(int classId, int sectionId, CancellationToken ct)
    {
        if (IsAdmin()) return;

        var employeeId = _user.EmployeeId ?? throw DomainException.Forbidden("Only teachers can mark class attendance.");
        var assigned = await _db.TeacherAssignments.AnyAsync(t =>
            t.SchoolId == SchoolId && t.EmployeeId == employeeId && t.ClassId == classId &&
            t.SectionId == sectionId && !t.IsDeleted, ct);

        if (!assigned)
            throw DomainException.Forbidden("You are not assigned to this class and section.");
    }

    private bool IsAdmin() =>
        AdminRoles.Contains(_user.Roles.FirstOrDefault() ?? string.Empty) || _user.HasPermission(Permissions.SettingsManage);

    private async Task EnsureCanReadStudentAsync(int studentId, CancellationToken ct)
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

        if (!_user.HasPermission(Permissions.AttendanceRead))
        {
            var employeeId = _user.EmployeeId;
            var teaches = employeeId is not null && await (
                from t in _db.TeacherAssignments
                join s in _db.Students on t.ClassId equals s.ClassId
                where t.EmployeeId == employeeId && s.Id == studentId && !t.IsDeleted
                select t.Id).AnyAsync(ct);

            if (!teaches) throw DomainException.Forbidden();
        }
    }

    private async Task<int> ResolveEmployeeIdAsync(int? requested, CancellationToken ct)
    {
        var employeeId = requested ?? _user.EmployeeId;
        if (employeeId is null) throw DomainException.Validation("Employee id is required.");

        if (requested is not null && requested != _user.EmployeeId && !IsAdmin())
            throw DomainException.Forbidden("You can only record your own attendance.");

        var exists = await _db.Employees.AnyAsync(e => e.Id == employeeId && e.SchoolId == SchoolId && !e.IsDeleted, ct);
        if (!exists) throw DomainException.NotFound("Employee");

        return employeeId.Value;
    }

    private async Task<StaffAttendanceDto> MapStaffAsync(EmployeeAttendance record, CancellationToken ct)
    {
        var employee = await _db.Employees.FirstAsync(e => e.Id == record.EmployeeId, ct);
        return new StaffAttendanceDto
        {
            EmployeeId = employee.Id, EmployeeName = employee.FullName, EmployeeNo = employee.EmployeeNo,
            Date = record.AttendanceDate, CheckInUtc = record.CheckInUtc, CheckOutUtc = record.CheckOutUtc,
            Status = record.Status, IsLate = record.CheckInUtc?.TimeOfDay > LateAfter
        };
    }
}
