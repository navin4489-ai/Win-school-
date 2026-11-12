namespace SchoolErp.Application.Common;

/// <summary>
/// Permission codes are stored in the database and embedded in tokens.
/// Format: &lt;module&gt;.&lt;action&gt;.
/// </summary>
public static class Permissions
{
    public const string StudentsRead = "students.read";
    public const string StudentsWrite = "students.write";
    public const string AdmissionsManage = "admissions.manage";
    public const string ParentsRead = "parents.read";
    public const string FeesRead = "fees.read";
    public const string FeesCollect = "fees.collect";
    public const string FeesRefund = "fees.refund";
    public const string AttendanceRead = "attendance.read";
    public const string AttendanceMark = "attendance.mark";
    public const string ExamsRead = "exams.read";
    public const string ExamsManage = "exams.manage";
    public const string HomeworkManage = "homework.manage";
    public const string HrRead = "hr.read";
    public const string HrManage = "hr.manage";
    public const string TransportManage = "transport.manage";
    public const string LibraryManage = "library.manage";
    public const string InventoryManage = "inventory.manage";
    public const string ReportsRead = "reports.read";
    public const string SettingsManage = "settings.manage";
    public const string UsersManage = "users.manage";
    public const string AuditRead = "audit.read";
    public const string LmsManage = "lms.manage";
    public const string CommunicationManage = "communication.manage";

    public static readonly IReadOnlyList<(string Code, string Module, string Action, string Description)> All =
    new List<(string, string, string, string)>
    {
        (StudentsRead, "Students", "Read", "View student records"),
        (StudentsWrite, "Students", "Write", "Create, update, promote and withdraw students"),
        (AdmissionsManage, "Admissions", "Manage", "Process enquiries, applications and enrolment"),
        (ParentsRead, "Parents", "Read", "View parent and guardian records"),
        (FeesRead, "Fees", "Read", "View fee structures, ledgers and reports"),
        (FeesCollect, "Fees", "Collect", "Record payments and issue receipts"),
        (FeesRefund, "Fees", "Refund", "Approve and process refunds"),
        (AttendanceRead, "Attendance", "Read", "View attendance records"),
        (AttendanceMark, "Attendance", "Mark", "Mark and correct attendance"),
        (ExamsRead, "Exams", "Read", "View examinations, marks and report cards"),
        (ExamsManage, "Exams", "Manage", "Configure exams, enter/approve marks and publish results"),
        (HomeworkManage, "Homework", "Manage", "Create, grade and manage homework"),
        (HrRead, "HR", "Read", "View employees, leave and payroll"),
        (HrManage, "HR", "Manage", "Manage employees, leave approvals and payroll"),
        (TransportManage, "Transport", "Manage", "Manage vehicles, routes and allocations"),
        (LibraryManage, "Library", "Manage", "Manage catalogue, issues and returns"),
        (InventoryManage, "Inventory", "Manage", "Manage stock, vendors and purchase orders"),
        (ReportsRead, "Reports", "Read", "Generate and export reports"),
        (SettingsManage, "Settings", "Manage", "Configure school, sessions, classes and permissions"),
        (UsersManage, "Users", "Manage", "Manage user accounts and role assignments"),
        (AuditRead, "Audit", "Read", "View audit logs"),
        (LmsManage, "LMS", "Manage", "Publish study material and assessments"),
        (CommunicationManage, "Communication", "Manage", "Send announcements and notifications")
    };
}

public static class RoleCodes
{
    public const string SuperAdmin = "SuperAdmin";
    public const string SchoolOwner = "SchoolOwner";
    public const string Principal = "Principal";
    public const string Administrator = "Administrator";
    public const string Accountant = "Accountant";
    public const string AdmissionManager = "AdmissionManager";
    public const string Teacher = "Teacher";
    public const string ClassCoordinator = "ClassCoordinator";
    public const string Student = "Student";
    public const string Parent = "Parent";
    public const string HrManager = "HrManager";
    public const string Librarian = "Librarian";
    public const string TransportManager = "TransportManager";
    public const string TransportStaff = "TransportStaff";
}
