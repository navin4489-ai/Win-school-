using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;

namespace SchoolErp.Infrastructure.Persistence;

/// <summary>
/// Single application DbContext for the modular monolith.
/// Tenant isolation is enforced twice: global query filters on reads and guards in
/// <see cref="SaveChangesAsync"/> on writes. Services must never remove those guards.
/// </summary>
public sealed class AppDbContext : DbContext, IAppDbContext
{
    private static readonly HashSet<string> RedactedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(UserAccount.PasswordHash),
        nameof(UserAccount.PasswordResetTokenHash),
        nameof(UserAccount.RefreshTokenHash),
        nameof(RefreshToken.TokenHash),
        nameof(RefreshToken.ReplacedByTokenHash)
    };

    private static readonly Dictionary<string, Type> ForeignKeyTargets = new(StringComparer.Ordinal)
    {
        ["School"] = typeof(School),
        ["Branch"] = typeof(Branch),
        ["AcademicSession"] = typeof(AcademicSession),
        ["Class"] = typeof(SchoolClass),
        ["Section"] = typeof(Section),
        ["Subject"] = typeof(Subject),
        ["Teacher"] = typeof(Employee),
        ["Employee"] = typeof(Employee),
        ["HeadEmployee"] = typeof(Employee),
        ["ClassTeacher"] = typeof(Employee),
        ["Student"] = typeof(Student),
        ["Guardian"] = typeof(Guardian),
        ["Admission"] = typeof(Admission),
        ["Examination"] = typeof(Examination),
        ["ExamSubject"] = typeof(ExamSubject),
        ["Homework"] = typeof(Homework),
        ["FeeCategory"] = typeof(FeeCategory),
        ["FeeStructure"] = typeof(FeeStructure),
        ["StudentFeeAssignment"] = typeof(StudentFeeAssignment),
        ["FeeInstallment"] = typeof(FeeInstallment),
        ["Payment"] = typeof(Payment),
        ["Discount"] = typeof(Discount),
        ["Refund"] = typeof(Refund),
        ["ExpenseCategory"] = typeof(ExpenseCategory),
        ["SalaryComponent"] = typeof(SalaryComponent),
        ["PayrollRun"] = typeof(PayrollRun),
        ["Department"] = typeof(Department),
        ["Designation"] = typeof(Designation),
        ["TransportRoute"] = typeof(TransportRoute),
        ["TransportStop"] = typeof(TransportStop),
        ["Vehicle"] = typeof(Vehicle),
        ["Book"] = typeof(Book),
        ["InventoryItem"] = typeof(InventoryItem),
        ["Vendor"] = typeof(Vendor),
        ["PurchaseOrder"] = typeof(PurchaseOrder),
        ["Document"] = typeof(Document),
        ["UserAccount"] = typeof(UserAccount),
        ["Role"] = typeof(Role),
        ["Permission"] = typeof(Permission),
        ["Notification"] = typeof(Notification),
        ["FeePenalty"] = typeof(FeePenalty)
    };

    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly INotificationPublisher _publisher;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser,
        IDateTimeProvider clock, INotificationPublisher publisher)
        : base(options)
    {
        _currentUser = currentUser;
        _clock = clock;
        _publisher = publisher;
    }

    // The filter reads this context-scoped value, never a captured request value.
    private int? CurrentSchoolId => _currentUser.SchoolId;

    private bool IsPostgres => Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;

    /// <summary>
    /// Index filters are written in SQL Server syntax; translate them for PostgreSQL
    /// (quoted identifiers and real booleans).
    /// </summary>
    private string Sql(string sqlServerFilter) => !IsPostgres
        ? sqlServerFilter
        : System.Text.RegularExpressions.Regex.Replace(sqlServerFilter, @"\[(\w+)\]", "\"$1\"")
            .Replace("\"IsDeleted\" = 0", "\"IsDeleted\" = false");

    // Background jobs and start-up seeding run without a request and see every school.
    // A web request without a school (anonymous, or a malformed token) sees nothing.
    private bool IsSystemContext => _currentUser.IsSystemContext;

    // --- IAppDbContext ---
    public DbSet<School> Schools => Set<School>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<AcademicSession> AcademicSessions => Set<AcademicSession>();
    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<ClassSubject> ClassSubjects => Set<ClassSubject>();
    public DbSet<TeacherAssignment> TeacherAssignments => Set<TeacherAssignment>();
    public DbSet<TimetableEntry> TimetableEntries => Set<TimetableEntry>();
    public DbSet<Examination> Examinations => Set<Examination>();
    public DbSet<ExamSubject> ExamSubjects => Set<ExamSubject>();
    public DbSet<StudentMark> StudentMarks => Set<StudentMark>();
    public DbSet<ReportCard> ReportCards => Set<ReportCard>();
    public DbSet<Homework> Homework => Set<Homework>();
    public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
    public DbSet<StudyMaterial> StudyMaterials => Set<StudyMaterial>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<StudentEnrollment> StudentEnrollments => Set<StudentEnrollment>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<StudentParent> StudentParents => Set<StudentParent>();
    public DbSet<Admission> Admissions => Set<Admission>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();
    public DbSet<FeeCategory> FeeCategories => Set<FeeCategory>();
    public DbSet<FeeStructure> FeeStructures => Set<FeeStructure>();
    public DbSet<FeeStructureItem> FeeStructureItems => Set<FeeStructureItem>();
    public DbSet<StudentFeeAssignment> StudentFeeAssignments => Set<StudentFeeAssignment>();
    public DbSet<FeeInstallment> FeeInstallments => Set<FeeInstallment>();
    public DbSet<FeePenalty> FeePenalties => Set<FeePenalty>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<StudentDiscount> StudentDiscounts => Set<StudentDiscount>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<SalaryComponent> SalaryComponents => Set<SalaryComponent>();
    public DbSet<EmployeeSalaryComponent> EmployeeSalaryComponents => Set<EmployeeSalaryComponent>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<Payslip> Payslips => Set<Payslip>();
    public DbSet<StudentAttendance> StudentAttendances => Set<StudentAttendance>();
    public DbSet<EmployeeAttendance> EmployeeAttendances => Set<EmployeeAttendance>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<TransportRoute> TransportRoutes => Set<TransportRoute>();
    public DbSet<TransportStop> TransportStops => Set<TransportStop>();
    public DbSet<TransportAllocation> TransportAllocations => Set<TransportAllocation>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookTransaction> BookTransactions => Set<BookTransaction>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<StudentDocument> StudentDocuments => Set<StudentDocument>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    void IAppDbContext.Add<TEntity>(TEntity entity) => Set<TEntity>().Add(entity);
    void IAppDbContext.Remove<TEntity>(TEntity entity) => Set<TEntity>().Remove(entity);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var audits = PrepareChanges();
        // Notifications are published from here rather than from every service that
        // creates one, so no code path can create a notification without reaching
        // the live subscribers.
        var pendingNotifications = ChangeTracker.Entries<Notification>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();

        var result = await base.SaveChangesAsync(cancellationToken);

        if (audits.Count > 0)
        {
            foreach (var audit in audits)
            {
                var id = audit.Entry.Property("Id").CurrentValue?.ToString();
                var schoolId = audit.Entry.Entity is IAuditable auditable ? auditable.SchoolId : 0;

                AuditLogs.Add(new AuditLog
                {
                    SchoolId = schoolId,
                    UserAccountId = _currentUser.UserId,
                    UserEmail = _currentUser.Email,
                    Action = audit.Action,
                    EntityName = audit.Entry.Entity.GetType().Name,
                    EntityId = id,
                    BeforeJson = audit.Before,
                    AfterJson = audit.After,
                    IpAddress = _currentUser.IpAddress,
                    UserAgent = _currentUser.UserAgent,
                    CorrelationId = Activity.Current?.Id,
                    CreatedAtUtc = _clock.UtcNow
                });
            }

            await base.SaveChangesAsync(cancellationToken);
        }

        foreach (var notification in pendingNotifications)
        {
            try
            {
                _publisher.Publish(notification);
            }
            catch (Exception)
            {
                // A live-notification failure must never fail the request that wrote the row.
            }
        }

        return result;
    }

    private List<PendingAudit> PrepareChanges()
    {
        var audits = new List<PendingAudit>();
        var now = _clock.UtcNow;
        var actor = _currentUser.Email ?? "system";
        var schoolId = _currentUser.SchoolId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog or Notification) continue;

            if (entry.Entity is IAuditable auditable)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        if (auditable.SchoolId == 0)
                        {
                            auditable.SchoolId = schoolId
                                ?? throw new DomainException("tenant_required",
                                    "A school context is required to create this record.", 400);
                        }

                        // Only default the branch for rows of the caller's own school; a super
                        // admin creating another school's rows must not stamp them with theirs.
                        if (auditable.BranchId is null && auditable.SchoolId == schoolId)
                            auditable.BranchId = _currentUser.BranchId;
                        if (auditable.CreatedAtUtc == default) auditable.CreatedAtUtc = now;
                        auditable.CreatedBy ??= actor;
                        audits.Add(new PendingAudit(entry, AuditActions.Create, null, Snapshot(entry, current: true)));
                        break;

                    case EntityState.Modified:
                        // Only the platform operator may change another school's records, and
                        // only through the explicitly cross-school platform endpoints.
                        if (schoolId.HasValue && auditable.SchoolId != schoolId.Value && !_currentUser.IsPlatformAdmin)
                            throw DomainException.Forbidden("Cannot modify a record that belongs to another school.");

                        auditable.ModifiedAtUtc = now;
                        auditable.ModifiedBy = actor;
                        audits.Add(new PendingAudit(entry, AuditActions.Update,
                            Snapshot(entry, current: false), Snapshot(entry, current: true)));
                        break;

                    case EntityState.Deleted:
                        entry.State = EntityState.Modified;
                        auditable.IsDeleted = true;
                        auditable.ModifiedAtUtc = now;
                        auditable.ModifiedBy = actor;
                        audits.Add(new PendingAudit(entry, AuditActions.Delete,
                            Snapshot(entry, current: false), Snapshot(entry, current: true)));
                        break;
                }

                // PostgreSQL has no rowversion type, so the application issues the
                // concurrency token itself. SQL Server generates it in the database.
                if (IsPostgres && entry.State is EntityState.Added or EntityState.Modified)
                    auditable.RowVersion = Guid.NewGuid().ToByteArray();
            }
        }

        return audits;
    }

    private static string? Snapshot(EntityEntry entry, bool current)
    {
        var values = current ? entry.CurrentValues : entry.OriginalValues;
        if (values is null) return null;

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in values.Properties)
        {
            if (RedactedProperties.Contains(property.Name))
            {
                payload[property.Name] = "***";
                continue;
            }

            var value = values[property];
            payload[property.Name] = value switch
            {
                null => null,
                byte[] bytes => Convert.ToBase64String(bytes),
                DateOnly date => date.ToString("yyyy-MM-dd"),
                TimeSpan time => time.ToString(@"hh\:mm\:ss"),
                _ => value
            };
        }

        return JsonSerializer.Serialize(payload);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Keep table names identical to the domain type names so the hand-maintained
        // database/schema.sql and EF's model agree (EF would otherwise pluralise DbSets).
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            modelBuilder.Entity(entityType.ClrType).ToTable(entityType.ClrType.Name);

        ApplyColumnConventions(modelBuilder);
        ApplyRelationships(modelBuilder);
        ApplyUniqueIndexes(modelBuilder);
        ApplyTenantFilters(modelBuilder);
    }

    private void ApplyColumnConventions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                {
                    if (property.Name is "Latitude" or "Longitude")
                    {
                        property.SetPrecision(9);
                        property.SetScale(6);
                    }
                    else
                    {
                        property.SetPrecision(18);
                        property.SetScale(2);
                    }
                }

                if (property.ClrType != typeof(string) || property.GetMaxLength() is not null) continue;

                var length = property.Name switch
                {
                    "Email" or "NormalizedEmail" => 256,
                    "Phone" or "GuardianPhone" or "DriverPhone" or "AlternatePhone" => 32,
                    "Code" or "CurrencyCode" or "Country" => 32,
                    "Name" or "FullName" or "LegalName" or "Title" or "GuardianName" or "DriverName"
                        or "VendorName" or "ContactPerson" or "FatherName" or "MotherName" => 200,
                    "AdmissionNo" or "EmployeeNo" or "ApplicationNo" or "OrderNo" or "ReceiptNo"
                        or "VoucherNo" or "RegistrationNo" or "ReferenceNo" or "GstNumber"
                        or "IdempotencyKey" or "DriverLicenceNo" or "Isbn" => 64,
                    "NormalizedUserName" => 256,
                    "TokenHash" or "ReplacedByTokenHash" => 128,
                    "PasswordHash" or "PasswordResetTokenHash" or "RefreshTokenHash" or "Checksum" => 512,
                    "ContentType" or "StoragePath" or "PublicUrl" or "FileUrl" or "PhotoUrl"
                        or "LogoUrl" or "AttachmentUrl" or "VideoUrl" => 512,
                    "Address" or "AddressLine1" or "AddressLine2" or "Remarks" or "Description"
                        or "Reason" or "Notes" or "Feedback" or "Error" or "FailureReason"
                        or "RawResponse" or "BeforeJson" or "AfterJson" or "UserAgent" => 2000,
                    "IpAddress" or "CorrelationId" => 128,
                    "Grade" or "ResultStatus" => 16,
                    _ => 256
                };
                property.SetMaxLength(length);
            }

            if (typeof(IAuditable).IsAssignableFrom(entityType.ClrType))
            {
                var rowVersion = modelBuilder.Entity(entityType.ClrType).Property(nameof(IAuditable.RowVersion));
                if (IsPostgres) rowVersion.IsConcurrencyToken();
                else rowVersion.IsRowVersion();
            }
        }
    }

    private static void ApplyRelationships(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (entityType.IsOwned()) continue;

            foreach (var property in clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType != typeof(int) && property.PropertyType != typeof(int?)) continue;
                if (property.Name == "Id" || !property.Name.EndsWith("Id", StringComparison.Ordinal)) continue;

                var prefix = property.Name[..^2];
                if (!ForeignKeyTargets.TryGetValue(prefix, out var target)) continue;
                if (target == clrType) continue;
                // Audit rows for failed logins can have no known tenant (SchoolId = 0).
                if (clrType == typeof(AuditLog) && property.Name == nameof(AuditLog.SchoolId)) continue;

                modelBuilder.Entity(clrType)
                    .HasOne(target)
                    .WithMany()
                    .HasForeignKey(property.Name)
                    .OnDelete(DeleteBehavior.Restrict);
            }
        }
    }

    private void ApplyUniqueIndexes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<School>().HasIndex(s => s.Code).IsUnique();
        modelBuilder.Entity<Permission>().HasIndex(p => p.Code).IsUnique();
        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.TokenHash).IsUnique();

        modelBuilder.Entity<Branch>().HasIndex(b => new { b.SchoolId, b.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<AcademicSession>().HasIndex(s => new { s.SchoolId, s.Name }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<SchoolClass>().HasIndex(c => new { c.SchoolId, c.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<Section>().HasIndex(s => new { s.SchoolId, s.ClassId, s.Name }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<Subject>().HasIndex(s => new { s.SchoolId, s.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<ClassSubject>().HasIndex(cs => new { cs.SchoolId, cs.ClassId, cs.SubjectId })
            .IsUnique().HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<TimetableEntry>()
            .HasIndex(t => new { t.SchoolId, t.SectionId, t.DayOfWeek, t.PeriodNo })
            .IsUnique().HasFilter(Sql("[IsDeleted] = 0"));

        modelBuilder.Entity<Student>().HasIndex(s => new { s.SchoolId, s.AdmissionNo }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<StudentEnrollment>()
            .HasIndex(e => new { e.SchoolId, e.StudentId, e.AcademicSessionId })
            .IsUnique().HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<StudentParent>().HasIndex(sp => new { sp.StudentId, sp.GuardianId })
            .IsUnique().HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<Admission>().HasIndex(a => new { a.SchoolId, a.ApplicationNo }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<Employee>().HasIndex(e => new { e.SchoolId, e.EmployeeNo }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<Department>().HasIndex(d => new { d.SchoolId, d.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<Designation>().HasIndex(d => new { d.SchoolId, d.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<UserAccount>().HasIndex(u => new { u.SchoolId, u.NormalizedEmail }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<Role>().HasIndex(r => new { r.SchoolId, r.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<RolePermission>().HasIndex(rp => new { rp.RoleId, rp.PermissionId })
            .IsUnique();

        modelBuilder.Entity<FeeCategory>().HasIndex(f => new { f.SchoolId, f.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<FeeStructure>().HasIndex(f => new { f.SchoolId, f.AcademicSessionId, f.ClassId, f.Name })
            .IsUnique().HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<FeeStructureItem>()
            .HasIndex(i => new { i.FeeStructureId, i.FeeCategoryId }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<FeeInstallment>()
            .HasIndex(i => new { i.StudentFeeAssignmentId, i.Name }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        // One late-fee row per installment per day keeps the penalty job idempotent.
        modelBuilder.Entity<FeePenalty>()
            .HasIndex(p => new { p.FeeInstallmentId, p.PenaltyDate }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<Payment>().HasIndex(p => new { p.SchoolId, p.ReceiptNo }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<Payment>().HasIndex(p => new { p.SchoolId, p.IdempotencyKey }).IsUnique()
            .HasFilter(Sql("[IdempotencyKey] IS NOT NULL"));
        modelBuilder.Entity<Discount>().HasIndex(d => new { d.SchoolId, d.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<StudentDiscount>()
            .HasIndex(d => new { d.SchoolId, d.StudentId, d.DiscountId, d.AcademicSessionId })
            .IsUnique().HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<ExpenseCategory>().HasIndex(c => new { c.SchoolId, c.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<SalaryComponent>().HasIndex(c => new { c.SchoolId, c.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<PayrollRun>().HasIndex(p => new { p.SchoolId, p.Year, p.Month }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));

        modelBuilder.Entity<StudentAttendance>()
            .HasIndex(a => new { a.SchoolId, a.StudentId, a.AttendanceDate }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<EmployeeAttendance>()
            .HasIndex(a => new { a.SchoolId, a.EmployeeId, a.AttendanceDate }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));

        modelBuilder.Entity<Vehicle>().HasIndex(v => new { v.SchoolId, v.RegistrationNo }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<TransportRoute>().HasIndex(r => new { r.SchoolId, r.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<Book>().HasIndex(b => new { b.SchoolId, b.Isbn }).IsUnique()
            .HasFilter(Sql("[Isbn] IS NOT NULL AND [IsDeleted] = 0"));
        modelBuilder.Entity<InventoryItem>().HasIndex(i => new { i.SchoolId, i.Code }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<PurchaseOrder>().HasIndex(p => new { p.SchoolId, p.OrderNo }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));

        modelBuilder.Entity<Examination>()
            .HasIndex(e => new { e.SchoolId, e.AcademicSessionId, e.Name }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<ExamSubject>()
            .HasIndex(es => new { es.ExaminationId, es.ClassId, es.SubjectId }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<StudentMark>()
            .HasIndex(m => new { m.ExaminationId, m.ExamSubjectId, m.StudentId }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
        modelBuilder.Entity<ReportCard>().HasIndex(r => new { r.ExaminationId, r.StudentId }).IsUnique()
            .HasFilter(Sql("[IsDeleted] = 0"));
    }

    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        var method = typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter),
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IAuditable).IsAssignableFrom(entityType.ClrType)) continue;
            method.MakeGenericMethod(entityType.ClrType).Invoke(this, new object[] { modelBuilder });
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IAuditable
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => !e.IsDeleted && (IsSystemContext || e.SchoolId == CurrentSchoolId));
    }

    private sealed record PendingAudit(EntityEntry Entry, string Action, string? Before, string? After);
}
