namespace SchoolErp.Application.Common;

/// <summary>
/// The standard roles every school starts with, and the permissions each one carries.
/// Used by the demo seed and by the super admin when a new school is created, so all
/// schools start from the same, reviewed baseline.
/// </summary>
public static class RoleTemplates
{
    public static readonly IReadOnlyList<(string Code, string Name)> Roles = new[]
    {
        (RoleCodes.SchoolOwner, "School Owner"),
        (RoleCodes.Principal, "Principal"),
        (RoleCodes.Administrator, "Administrator"),
        (RoleCodes.Accountant, "Accountant"),
        (RoleCodes.AdmissionManager, "Admission Manager"),
        (RoleCodes.Teacher, "Teacher"),
        (RoleCodes.ClassCoordinator, "Class Coordinator"),
        (RoleCodes.Student, "Student"),
        (RoleCodes.Parent, "Parent"),
        (RoleCodes.HrManager, "HR Manager"),
        (RoleCodes.Librarian, "Librarian"),
        (RoleCodes.TransportManager, "Transport Manager"),
        (RoleCodes.TransportStaff, "Transport Staff")
    };

    /// <summary>Permission codes per role. SuperAdmin and SchoolOwner receive everything.</summary>
    public static IReadOnlyList<string> PermissionsFor(string roleCode) => roleCode switch
    {
        RoleCodes.SuperAdmin or RoleCodes.SchoolOwner => Permissions.All.Select(p => p.Code).ToArray(),
        RoleCodes.Principal => new[]
        {
            Permissions.StudentsRead, Permissions.StudentsWrite, Permissions.AdmissionsManage,
            Permissions.ParentsRead, Permissions.FeesRead, Permissions.FeesRefund, Permissions.AttendanceRead,
            Permissions.ExamsRead, Permissions.ExamsManage, Permissions.HomeworkManage, Permissions.HrRead,
            Permissions.HrManage, Permissions.TransportManage, Permissions.LibraryManage,
            Permissions.InventoryManage, Permissions.ReportsRead, Permissions.SettingsManage,
            Permissions.UsersManage, Permissions.AuditRead, Permissions.LmsManage, Permissions.CommunicationManage
        },
        RoleCodes.Administrator => new[]
        {
            Permissions.StudentsRead, Permissions.StudentsWrite, Permissions.AdmissionsManage,
            Permissions.ParentsRead, Permissions.FeesRead, Permissions.FeesCollect, Permissions.AttendanceRead,
            Permissions.AttendanceMark, Permissions.ExamsRead, Permissions.ExamsManage, Permissions.HrRead,
            Permissions.TransportManage, Permissions.LibraryManage, Permissions.InventoryManage,
            Permissions.ReportsRead, Permissions.SettingsManage, Permissions.UsersManage,
            Permissions.CommunicationManage
        },
        RoleCodes.Accountant => new[]
        {
            Permissions.StudentsRead, Permissions.ParentsRead, Permissions.FeesRead, Permissions.FeesCollect,
            Permissions.FeesRefund, Permissions.HrRead, Permissions.ReportsRead
        },
        RoleCodes.AdmissionManager => new[]
        {
            Permissions.StudentsRead, Permissions.AdmissionsManage, Permissions.ParentsRead,
            Permissions.FeesRead, Permissions.ReportsRead, Permissions.CommunicationManage
        },
        RoleCodes.Teacher => new[]
        {
            Permissions.StudentsRead, Permissions.ParentsRead, Permissions.AttendanceRead,
            Permissions.AttendanceMark, Permissions.ExamsRead, Permissions.ExamsManage,
            Permissions.HomeworkManage, Permissions.LmsManage
        },
        RoleCodes.ClassCoordinator => new[]
        {
            Permissions.StudentsRead, Permissions.ParentsRead, Permissions.AttendanceRead,
            Permissions.AttendanceMark, Permissions.ExamsRead, Permissions.ExamsManage,
            Permissions.HomeworkManage, Permissions.LmsManage, Permissions.ReportsRead,
            Permissions.CommunicationManage
        },
        RoleCodes.Student => new[] { Permissions.AttendanceRead, Permissions.ExamsRead, Permissions.FeesRead },
        RoleCodes.Parent => new[]
        {
            Permissions.ParentsRead, Permissions.AttendanceRead, Permissions.ExamsRead, Permissions.FeesRead
        },
        RoleCodes.HrManager => new[]
        {
            Permissions.HrRead, Permissions.HrManage, Permissions.ReportsRead, Permissions.UsersManage
        },
        RoleCodes.Librarian => new[] { Permissions.LibraryManage, Permissions.StudentsRead },
        RoleCodes.TransportManager => new[]
        {
            Permissions.TransportManage, Permissions.StudentsRead, Permissions.ReportsRead
        },
        RoleCodes.TransportStaff => new[] { Permissions.TransportManage, Permissions.StudentsRead },
        _ => Array.Empty<string>()
    };
}

/// <summary>
/// Functional modules a super admin can switch on or off per school. A disabled module's
/// permissions are removed from every token issued for that school, so its menus disappear
/// and its endpoints answer 403. Core modules (students, settings, users, audit) are always on.
/// </summary>
public static class SchoolModules
{
    public static readonly IReadOnlyList<(string Key, string Label)> Toggleable = new[]
    {
        ("Admissions", "Admissions"),
        ("Fees", "Fees & payments"),
        ("Attendance", "Attendance"),
        ("Exams", "Examinations"),
        ("Homework", "Homework"),
        ("LMS", "Study material (LMS)"),
        ("HR", "HR & payroll"),
        ("Transport", "Transport"),
        ("Library", "Library"),
        ("Inventory", "Inventory"),
        ("Reports", "Reports"),
        ("Communication", "Notifications & broadcasts")
    };

    private const string NoneMarker = "-";

    public static IReadOnlySet<string> Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return Toggleable.Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(m => m != NoneMarker)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Stores the selection. An empty selection is stored as a marker rather than null,
    /// because null means "everything on" for schools created before modules existed.
    /// </summary>
    public static string Serialize(IEnumerable<string>? modules)
    {
        var known = Toggleable.Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = (modules ?? Array.Empty<string>())
            .Where(known.Contains)
            .Select(m => Toggleable.First(t => t.Key.Equals(m, StringComparison.OrdinalIgnoreCase)).Key)
            .Distinct()
            .ToList();

        return selected.Count == 0 ? NoneMarker : string.Join(',', selected);
    }

    /// <summary>True when the permission belongs to a module that is switched on (or is core).</summary>
    public static bool Allows(IReadOnlySet<string> enabled, string permissionCode)
    {
        var module = Permissions.All.FirstOrDefault(p => p.Code == permissionCode).Module;
        if (string.IsNullOrEmpty(module)) return true;

        var toggleable = Toggleable.Any(t => t.Key.Equals(module, StringComparison.OrdinalIgnoreCase));
        return !toggleable || enabled.Contains(module);
    }
}
