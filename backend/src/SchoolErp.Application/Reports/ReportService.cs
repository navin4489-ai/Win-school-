using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Reports;

public sealed class ReportFile
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "text/csv";
    public byte[] Content { get; init; } = Array.Empty<byte>();
}

public sealed class ReportTypeDto
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed class ReportService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTimeProvider _clock;

    public ReportService(IAppDbContext db, ICurrentUser user, IDateTimeProvider clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    public static IReadOnlyList<ReportTypeDto> Types { get; } = new List<ReportTypeDto>
    {
        new() { Key = "students", Name = "Student directory", Description = "Active students by class and section" },
        new() { Key = "admissions", Name = "Admissions", Description = "Applications in a date range" },
        new() { Key = "fees", Name = "Fee collection", Description = "Receipt-level collection register" },
        new() { Key = "outstanding", Name = "Outstanding fees", Description = "Student-wise outstanding balances" },
        new() { Key = "attendance", Name = "Attendance register", Description = "Daily attendance records" },
        new() { Key = "employees", Name = "Employee directory", Description = "Active and former staff" },
        new() { Key = "library", Name = "Library stock", Description = "Catalogue and availability" },
        new() { Key = "inventory", Name = "Inventory stock", Description = "Stock levels and valuation" }
    };

    public async Task<ReportFile> ExportAsync(string type, DateOnly? from, DateOnly? to, int? classId,
        int? sessionId, CancellationToken ct = default)
    {
        var start = from ?? new DateOnly(_clock.Today.Year, _clock.Today.Month, 1);
        var end = to ?? _clock.Today;
        if (end < start) throw DomainException.Validation("The end date must be on or after the start date.");

        var (fileName, rows) = type.ToLowerInvariant() switch
        {
            "students" => ("students.csv", await StudentRowsAsync(classId, sessionId, ct)),
            "admissions" => ("admissions.csv", await AdmissionRowsAsync(start, end, ct)),
            "fees" => ("fee-collection.csv", await FeeRowsAsync(start, end, ct)),
            "outstanding" => ("outstanding.csv", await OutstandingRowsAsync(classId, ct)),
            "attendance" => ("attendance.csv", await AttendanceRowsAsync(start, end, classId, ct)),
            "employees" => ("employees.csv", await EmployeeRowsAsync(ct)),
            "library" => ("library.csv", await LibraryRowsAsync(ct)),
            "inventory" => ("inventory.csv", await InventoryRowsAsync(ct)),
            _ => throw DomainException.Validation("Unknown report type.")
        };

        var csv = new StringBuilder();
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(',', row.Select(Csv)));
        }

        _db.Add(new AuditLog
        {
            SchoolId = SchoolId,
            UserAccountId = _user.UserId,
            UserEmail = _user.Email,
            Action = AuditActions.Export,
            EntityName = "Report",
            EntityId = type,
            AfterJson = $"{{\"from\":\"{start:yyyy-MM-dd}\",\"to\":\"{end:yyyy-MM-dd}\",\"rows\":{Math.Max(0, rows.Count - 1)}}}",
            IpAddress = _user.IpAddress,
            UserAgent = _user.UserAgent,
            CreatedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return new ReportFile { FileName = fileName, Content = bytes };
    }

    private async Task<List<string[]>> StudentRowsAsync(int? classId, int? sessionId, CancellationToken ct)
    {
        var query =
            from s in _db.Students
            join klass in _db.Classes on s.ClassId equals klass.Id
            join section in _db.Sections on s.SectionId equals section.Id
            where s.SchoolId == SchoolId && s.Status == StudentStatus.Active && !s.IsDeleted
            select new { s, ClassName = klass.Name, SectionName = section.Name };

        if (classId is > 0) query = query.Where(x => x.s.ClassId == classId);
        if (sessionId is > 0) query = query.Where(x => x.s.AcademicSessionId == sessionId);

        var rows = await query.OrderBy(x => x.ClassName).ThenBy(x => x.s.FirstName).ToListAsync(ct);
        var guardians = await PrimaryGuardiansAsync(rows.Select(r => r.s.Id).ToList(), ct);

        var result = new List<string[]> { new[] { "Admission No", "Name", "Class", "Section", "Roll", "Gender", "Guardian", "Guardian Phone", "Status", "Admission Date" } };
        result.AddRange(rows.Select(x => new[]
        {
            x.s.AdmissionNo, x.s.FullName, x.ClassName, x.SectionName, x.s.RollNo ?? string.Empty,
            x.s.Gender.ToString(), guardians.GetValueOrDefault(x.s.Id)?.GuardianName ?? string.Empty,
            guardians.GetValueOrDefault(x.s.Id)?.Phone ?? string.Empty,
            x.s.Status.ToString(), x.s.AdmissionDate.ToString("yyyy-MM-dd")
        }));
        return result;
    }

    private async Task<List<string[]>> AdmissionRowsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await (
            from a in _db.Admissions
            where a.SchoolId == SchoolId && !a.IsDeleted && a.AppliedAtUtc >= start && a.AppliedAtUtc < end
            orderby a.AppliedAtUtc descending
            select a).ToListAsync(ct);

        var result = new List<string[]> { new[] { "Application No", "Student", "Guardian", "Phone", "Status", "Applied On", "Source" } };
        result.AddRange(rows.Select(a => new[]
        {
            a.ApplicationNo, a.FirstName + " " + a.LastName, a.GuardianName, a.GuardianPhone,
            a.Status.ToString(), a.AppliedAtUtc.ToString("yyyy-MM-dd HH:mm"), a.Source ?? string.Empty
        }));
        return result;
    }

    private async Task<List<string[]>> FeeRowsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await (
            from p in _db.Payments
            join s in _db.Students on p.StudentId equals s.Id
            where p.SchoolId == SchoolId && !p.IsDeleted && p.PaidAtUtc >= start && p.PaidAtUtc < end
            orderby p.PaidAtUtc descending
            select new { p, StudentName = s.FirstName + " " + s.LastName, s.AdmissionNo }).ToListAsync(ct);

        var result = new List<string[]> { new[] { "Receipt", "Date", "Student", "Admission No", "Amount", "Mode", "Status", "Reference", "Refunded" } };
        result.AddRange(rows.Select(x => new[]
        {
            x.p.ReceiptNo, x.p.PaidAtUtc.ToString("yyyy-MM-dd HH:mm"), x.StudentName, x.AdmissionNo,
            x.p.Amount.ToString("0.00"), x.p.Mode.ToString(), x.p.Status.ToString(),
            x.p.ReferenceNo ?? string.Empty, x.p.RefundedAmount.ToString("0.00")
        }));
        return result;
    }

    private async Task<List<string[]>> OutstandingRowsAsync(int? classId, CancellationToken ct)
    {
        var query =
            from i in _db.FeeInstallments
            join a in _db.StudentFeeAssignments on i.StudentFeeAssignmentId equals a.Id
            join s in _db.Students on a.StudentId equals s.Id
            join klass in _db.Classes on s.ClassId equals klass.Id
            where i.SchoolId == SchoolId && !i.IsDeleted && a.IsActive &&
                  i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived &&
                  i.Amount + i.PenaltyAmount > i.PaidAmount
            select new { i, s, ClassName = klass.Name };

        if (classId is > 0) query = query.Where(x => x.s.ClassId == classId);
        var rows = await query.ToListAsync(ct);

        var result = new List<string[]> { new[] { "Student", "Admission No", "Class", "Outstanding", "Oldest Due" } };
        result.AddRange(rows.GroupBy(x => x.s.Id).Select(g => new[]
        {
            g.First().s.FullName, g.First().s.AdmissionNo, g.First().ClassName,
            g.Sum(x => x.i.Amount + x.i.PenaltyAmount - x.i.PaidAmount).ToString("0.00"),
            g.Min(x => x.i.DueDate).ToString("yyyy-MM-dd")
        }));
        return result;
    }

    private async Task<List<string[]>> AttendanceRowsAsync(DateOnly from, DateOnly to, int? classId, CancellationToken ct)
    {
        var query =
            from a in _db.StudentAttendances
            join s in _db.Students on a.StudentId equals s.Id
            join klass in _db.Classes on a.ClassId equals klass.Id
            join section in _db.Sections on a.SectionId equals section.Id
            where a.SchoolId == SchoolId && !a.IsDeleted && a.AttendanceDate >= @from && a.AttendanceDate <= to
            select new { a, StudentName = s.FirstName + " " + s.LastName, s.AdmissionNo, ClassName = klass.Name, SectionName = section.Name };

        if (classId is > 0) query = query.Where(x => x.a.ClassId == classId);
        var rows = await query.OrderBy(x => x.a.AttendanceDate).ThenBy(x => x.StudentName).ToListAsync(ct);

        var result = new List<string[]> { new[] { "Date", "Student", "Admission No", "Class", "Section", "Status", "Remarks" } };
        result.AddRange(rows.Select(x => new[]
        {
            x.a.AttendanceDate.ToString("yyyy-MM-dd"), x.StudentName, x.AdmissionNo, x.ClassName,
            x.SectionName, x.a.Status.ToString(), x.a.Remarks ?? string.Empty
        }));
        return result;
    }

    private async Task<List<string[]>> EmployeeRowsAsync(CancellationToken ct)
    {
        var rows = await (
            from e in _db.Employees
            join d in _db.Departments on e.DepartmentId equals d.Id into departments
            from d in departments.DefaultIfEmpty()
            join des in _db.Designations on e.DesignationId equals des.Id into designations
            from des in designations.DefaultIfEmpty()
            where e.SchoolId == SchoolId && !e.IsDeleted
            orderby e.FirstName
            select new { e, Department = d == null ? null : d.Name, Designation = des == null ? null : des.Name }).ToListAsync(ct);

        var result = new List<string[]> { new[] { "Employee No", "Name", "Email", "Phone", "Department", "Designation", "Joining Date", "Status" } };
        result.AddRange(rows.Select(x => new[]
        {
            x.e.EmployeeNo, x.e.FullName, x.e.Email, x.e.Phone ?? string.Empty,
            x.Department ?? string.Empty, x.Designation ?? string.Empty,
            x.e.JoiningDate.ToString("yyyy-MM-dd"), x.e.Status.ToString()
        }));
        return result;
    }

    private async Task<List<string[]>> LibraryRowsAsync(CancellationToken ct)
    {
        var rows = await _db.Books.Where(b => b.SchoolId == SchoolId && !b.IsDeleted)
            .OrderBy(b => b.Title).ToListAsync(ct);

        var result = new List<string[]> { new[] { "Title", "ISBN", "Category", "Author", "Total", "Available", "Shelf" } };
        result.AddRange(rows.Select(b => new[]
        {
            b.Title, b.Isbn ?? string.Empty, b.Category ?? string.Empty, b.Author ?? string.Empty,
            b.TotalCopies.ToString(), b.AvailableCopies.ToString(), b.Shelf ?? string.Empty
        }));
        return result;
    }

    private async Task<List<string[]>> InventoryRowsAsync(CancellationToken ct)
    {
        var rows = await _db.InventoryItems.Where(i => i.SchoolId == SchoolId && !i.IsDeleted)
            .OrderBy(i => i.Name).ToListAsync(ct);

        var result = new List<string[]> { new[] { "Item", "Code", "Category", "Quantity", "Unit", "Minimum", "Unit Price", "Value", "Below Minimum" } };
        result.AddRange(rows.Select(i => new[]
        {
            i.Name, i.Code, i.Category ?? string.Empty, i.Quantity.ToString("0.##"), i.Unit,
            i.MinimumStock.ToString("0.##"), i.UnitPrice.ToString("0.00"),
            (i.Quantity * i.UnitPrice).ToString("0.00"), (i.Quantity < i.MinimumStock).ToString()
        }));
        return result;
    }

    private async Task<Dictionary<int, GuardianInfo>> PrimaryGuardiansAsync(List<int> studentIds, CancellationToken ct)
    {
        var rows = await (
            from sp in _db.StudentParents
            join g in _db.Guardians on sp.GuardianId equals g.Id
            where studentIds.Contains(sp.StudentId) && sp.IsPrimary && !sp.IsDeleted
            select new { sp.StudentId, g.FirstName, g.LastName, g.Phone }).ToListAsync(ct);

        return rows.GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g =>
        {
            var first = g.First();
            return new GuardianInfo(first.FirstName + " " + first.LastName, first.Phone);
        });
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private sealed record GuardianInfo(string GuardianName, string Phone);
}
