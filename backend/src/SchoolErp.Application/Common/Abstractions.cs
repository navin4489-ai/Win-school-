using Microsoft.EntityFrameworkCore;
using SchoolErp.Domain.Entities;

namespace SchoolErp.Application.Common;

public interface IAppDbContext
{
    DbSet<School> Schools { get; }
    DbSet<Branch> Branches { get; }
    DbSet<AcademicSession> AcademicSessions { get; }
    DbSet<SystemConfiguration> SystemConfigurations { get; }
    DbSet<Announcement> Announcements { get; }
    DbSet<SchoolClass> Classes { get; }
    DbSet<Section> Sections { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<ClassSubject> ClassSubjects { get; }
    DbSet<TeacherAssignment> TeacherAssignments { get; }
    DbSet<TimetableEntry> TimetableEntries { get; }
    DbSet<Examination> Examinations { get; }
    DbSet<ExamSubject> ExamSubjects { get; }
    DbSet<StudentMark> StudentMarks { get; }
    DbSet<ReportCard> ReportCards { get; }
    DbSet<Homework> Homework { get; }
    DbSet<AssignmentSubmission> AssignmentSubmissions { get; }
    DbSet<StudyMaterial> StudyMaterials { get; }
    DbSet<Student> Students { get; }
    DbSet<StudentEnrollment> StudentEnrollments { get; }
    DbSet<Guardian> Guardians { get; }
    DbSet<StudentParent> StudentParents { get; }
    DbSet<Admission> Admissions { get; }
    DbSet<Employee> Employees { get; }
    DbSet<Department> Departments { get; }
    DbSet<Designation> Designations { get; }
    DbSet<UserAccount> UserAccounts { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<LeaveApplication> LeaveApplications { get; }
    DbSet<FeeCategory> FeeCategories { get; }
    DbSet<FeeStructure> FeeStructures { get; }
    DbSet<FeeStructureItem> FeeStructureItems { get; }
    DbSet<StudentFeeAssignment> StudentFeeAssignments { get; }
    DbSet<FeeInstallment> FeeInstallments { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentTransaction> PaymentTransactions { get; }
    DbSet<Discount> Discounts { get; }
    DbSet<StudentDiscount> StudentDiscounts { get; }
    DbSet<Refund> Refunds { get; }
    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<SalaryComponent> SalaryComponents { get; }
    DbSet<EmployeeSalaryComponent> EmployeeSalaryComponents { get; }
    DbSet<PayrollRun> PayrollRuns { get; }
    DbSet<Payslip> Payslips { get; }
    DbSet<StudentAttendance> StudentAttendances { get; }
    DbSet<EmployeeAttendance> EmployeeAttendances { get; }
    DbSet<Vehicle> Vehicles { get; }
    DbSet<TransportRoute> TransportRoutes { get; }
    DbSet<TransportStop> TransportStops { get; }
    DbSet<TransportAllocation> TransportAllocations { get; }
    DbSet<Book> Books { get; }
    DbSet<BookTransaction> BookTransactions { get; }
    DbSet<InventoryItem> InventoryItems { get; }
    DbSet<Vendor> Vendors { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderItem> PurchaseOrderItems { get; }
    DbSet<StockTransaction> StockTransactions { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<Document> Documents { get; }
    DbSet<StudentDocument> StudentDocuments { get; }
    DbSet<AuditLog> AuditLogs { get; }

    void Add<TEntity>(TEntity entity) where TEntity : class;
    void Remove<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default);
}

public interface ICurrentUser
{
    int? UserId { get; }
    int? SchoolId { get; }
    int? BranchId { get; }
    int? EmployeeId { get; }
    int? GuardianId { get; }
    int? StudentId { get; }
    string? Email { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    bool IsAuthenticated { get; }
    IReadOnlyCollection<string> Permissions { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool HasPermission(string permission);
    bool IsInRole(string role);
}

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessToken CreateAccessToken(UserAccount user, string roleCode, IEnumerable<string> permissions);
    string CreateRefreshToken();
    string HashRefreshToken(string token);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateOnly Today { get; }
}

public sealed record StoredFile(string Path, string? Url);

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    string? GetDownloadUrl(string path, TimeSpan lifetime);
}

public interface INotificationSender
{
    Task SendAsync(Notification notification, string? recipient, CancellationToken ct = default);
}

public sealed record GatewayVerification(bool Success, string? GatewayPaymentId, string? FailureReason, string? RawResponse);

public interface IPaymentGateway
{
    string Name { get; }
    Task<GatewayVerification> VerifyAsync(string orderId, string paymentId, decimal amount, CancellationToken ct = default);
    Task<GatewayVerification> RefundAsync(string paymentId, decimal amount, CancellationToken ct = default);
}
