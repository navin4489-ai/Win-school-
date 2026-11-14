using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Application.Fees;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
using SchoolErp.Infrastructure.Persistence;
using Xunit;

namespace SchoolErp.Tests;

public sealed class FeeServiceTests
{
    [Fact]
    public async Task RecordPayment_PartiallyPays_ThenSettlesInstallment()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser { Permissions = new[] { Permissions.FeesCollect, Permissions.FeesRead } };
        await using var db = TestDb.Create(user, clock);
        var (student, installment) = await SeedAsync(db, user, clock);
        var service = new FeeService(db, new UnitOfWork(db), user, clock, new FakeGateway());

        var first = await service.RecordPaymentAsync(new RecordPaymentRequest
        {
            StudentId = student.Id,
            FeeInstallmentId = installment.Id,
            Amount = 500m,
            Mode = PaymentMode.Cash,
            IdempotencyKey = "idem-1"
        });

        Assert.StartsWith("RCPT-TEST-", first.ReceiptNo);
        Assert.Equal(500m, first.Amount);

        var afterFirst = await db.FeeInstallments.AsNoTracking().FirstAsync(i => i.Id == installment.Id);
        Assert.Equal(500m, afterFirst.PaidAmount);
        Assert.Equal(InstallmentStatus.PartiallyPaid, afterFirst.Status);

        var duplicate = await service.RecordPaymentAsync(new RecordPaymentRequest
        {
            StudentId = student.Id,
            FeeInstallmentId = installment.Id,
            Amount = 500m,
            Mode = PaymentMode.Cash,
            IdempotencyKey = "idem-1"
        });
        Assert.Equal(first.ReceiptNo, duplicate.ReceiptNo);

        var second = await service.RecordPaymentAsync(new RecordPaymentRequest
        {
            StudentId = student.Id,
            FeeInstallmentId = installment.Id,
            Amount = 500m,
            Mode = PaymentMode.Upi,
            IdempotencyKey = "idem-2"
        });

        Assert.NotEqual(first.ReceiptNo, second.ReceiptNo);
        var afterSecond = await db.FeeInstallments.AsNoTracking().FirstAsync(i => i.Id == installment.Id);
        Assert.Equal(InstallmentStatus.Paid, afterSecond.Status);
        Assert.Equal(1000m, afterSecond.PaidAmount);
    }

    [Fact]
    public async Task RecordPayment_RejectsOverpayment()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser { Permissions = new[] { Permissions.FeesCollect } };
        await using var db = TestDb.Create(user, clock);
        var (student, installment) = await SeedAsync(db, user, clock);
        var service = new FeeService(db, new UnitOfWork(db), user, clock, new FakeGateway());

        var error = await Assert.ThrowsAsync<DomainException>(() => service.RecordPaymentAsync(
            new RecordPaymentRequest
            {
                StudentId = student.Id,
                FeeInstallmentId = installment.Id,
                Amount = 1500m,
                Mode = PaymentMode.Cash
            }));

        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task CancelAssignment_IsBlockedOncePaid()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser { Permissions = new[] { Permissions.FeesCollect, Permissions.FeesRead } };
        await using var db = TestDb.Create(user, clock);
        var (student, installment) = await SeedAsync(db, user, clock);
        var service = new FeeService(db, new UnitOfWork(db), user, clock, new FakeGateway());

        await service.RecordPaymentAsync(new RecordPaymentRequest
        {
            StudentId = student.Id, FeeInstallmentId = installment.Id, Amount = 200m, Mode = PaymentMode.Cash
        });

        var error = await Assert.ThrowsAsync<DomainException>(() => service.CancelAssignmentAsync(
            installment.StudentFeeAssignmentId, new FeeReasonRequest { Reason = "Left school" }));

        Assert.Equal("conflict", error.Code);
        var assignment = await db.StudentFeeAssignments.AsNoTracking().FirstAsync(a => a.Id == installment.StudentFeeAssignmentId);
        Assert.True(assignment.IsActive);
    }

    [Fact]
    public async Task CancelAssignment_WithoutPayments_RemovesInstallments()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser { Permissions = new[] { Permissions.FeesCollect, Permissions.FeesRead } };
        await using var db = TestDb.Create(user, clock);
        var (_, installment) = await SeedAsync(db, user, clock);
        var service = new FeeService(db, new UnitOfWork(db), user, clock, new FakeGateway());

        var ledger = await service.CancelAssignmentAsync(installment.StudentFeeAssignmentId,
            new FeeReasonRequest { Reason = "Wrong plan" });

        Assert.Empty(ledger.Installments);
        Assert.Equal(0m, ledger.Balance);
        Assert.False(Assert.Single(ledger.Assignments).IsActive);
    }

    [Fact]
    public async Task WaiveInstallment_ReducesNetAndMarksWaived()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser { Permissions = new[] { Permissions.FeesCollect, Permissions.FeesRead } };
        await using var db = TestDb.Create(user, clock);
        var (student, installment) = await SeedAsync(db, user, clock);
        var service = new FeeService(db, new UnitOfWork(db), user, clock, new FakeGateway());

        await service.RecordPaymentAsync(new RecordPaymentRequest
        {
            StudentId = student.Id, FeeInstallmentId = installment.Id, Amount = 300m, Mode = PaymentMode.Cash
        });

        var ledger = await service.WaiveInstallmentAsync(installment.Id, new FeeReasonRequest { Reason = "Hardship" });

        var saved = await db.FeeInstallments.AsNoTracking().FirstAsync(i => i.Id == installment.Id);
        Assert.Equal(InstallmentStatus.Waived, saved.Status);
        Assert.Equal(300m, saved.PaidAmount);
        var assignment = await db.StudentFeeAssignments.AsNoTracking().FirstAsync(a => a.Id == installment.StudentFeeAssignmentId);
        Assert.Equal(300m, assignment.NetAmount);
        Assert.Equal(0m, ledger.Balance);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.WaiveInstallmentAsync(installment.Id, new FeeReasonRequest { Reason = "Again" }));
    }

    [Fact]
    public async Task UpdateInstallment_RejectsAmountBelowPaid()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser { Permissions = new[] { Permissions.FeesCollect, Permissions.FeesRead } };
        await using var db = TestDb.Create(user, clock);
        var (student, installment) = await SeedAsync(db, user, clock);
        var service = new FeeService(db, new UnitOfWork(db), user, clock, new FakeGateway());

        await service.RecordPaymentAsync(new RecordPaymentRequest
        {
            StudentId = student.Id, FeeInstallmentId = installment.Id, Amount = 600m, Mode = PaymentMode.Cash
        });

        var error = await Assert.ThrowsAsync<DomainException>(() => service.UpdateInstallmentAsync(installment.Id,
            new UpdateInstallmentRequest { Name = "Term 1", DueDate = clock.Today.AddDays(10), Amount = 500m }));
        Assert.Equal("validation_error", error.Code);

        await service.UpdateInstallmentAsync(installment.Id,
            new UpdateInstallmentRequest { Name = "Term 1", DueDate = clock.Today.AddDays(20), Amount = 600m });

        var saved = await db.FeeInstallments.AsNoTracking().FirstAsync(i => i.Id == installment.Id);
        Assert.Equal(600m, saved.Amount);
        Assert.Equal(InstallmentStatus.Paid, saved.Status);
        var assignment = await db.StudentFeeAssignments.AsNoTracking().FirstAsync(a => a.Id == installment.StudentFeeAssignmentId);
        Assert.Equal(600m, assignment.NetAmount);
    }

    [Fact]
    public async Task AddDiscount_ReducesLatestInstallmentsFirst()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser { Permissions = new[] { Permissions.FeesCollect, Permissions.FeesRead } };
        await using var db = TestDb.Create(user, clock);
        var (_, first) = await SeedAsync(db, user, clock);
        var service = new FeeService(db, new UnitOfWork(db), user, clock, new FakeGateway());

        var assignment = await db.StudentFeeAssignments.FirstAsync(a => a.Id == first.StudentFeeAssignmentId);
        assignment.TotalAmount = 2000m;
        assignment.NetAmount = 2000m;
        var second = new FeeInstallment
        {
            SchoolId = first.SchoolId, StudentFeeAssignmentId = assignment.Id, Name = "Term 2",
            DueDate = clock.Today.AddDays(40), Amount = 500m, Status = InstallmentStatus.Pending
        };
        var third = new FeeInstallment
        {
            SchoolId = first.SchoolId, StudentFeeAssignmentId = assignment.Id, Name = "Term 3",
            DueDate = clock.Today.AddDays(70), Amount = 500m, Status = InstallmentStatus.Pending
        };
        db.AddRange(second, third);
        await db.SaveChangesAsync();

        var tooMuch = await Assert.ThrowsAsync<DomainException>(() => service.AddDiscountAsync(assignment.Id,
            new FeeDiscountRequest { Amount = 2500m, Reason = "Too much" }));
        Assert.Equal("validation_error", tooMuch.Code);

        var ledger = await service.AddDiscountAsync(assignment.Id, new FeeDiscountRequest { Amount = 700m, Reason = "Sibling" });

        var rows = await db.FeeInstallments.AsNoTracking()
            .Where(i => i.StudentFeeAssignmentId == assignment.Id).ToDictionaryAsync(i => i.Name);
        Assert.Equal(1000m, rows["Term 1"].Amount);
        Assert.Equal(300m, rows["Term 2"].Amount);
        Assert.Equal(0m, rows["Term 3"].Amount);

        var saved = await db.StudentFeeAssignments.AsNoTracking().FirstAsync(a => a.Id == assignment.Id);
        Assert.Equal(700m, saved.DiscountAmount);
        Assert.Equal(1300m, saved.NetAmount);
        Assert.Equal(1300m, ledger.Balance);
    }

    private static async Task<(Student Student, FeeInstallment Installment)> SeedAsync(AppDbContext db,
        TestCurrentUser user, TestClock clock)
    {
        var school = new School { Code = "TEST", Name = "Test School" };
        db.Add(school);
        await db.SaveChangesAsync();
        user.SchoolId = school.Id;

        var branch = new Branch { SchoolId = school.Id, Name = "Main", Code = "MAIN", IsMainBranch = true };
        db.Add(branch);
        await db.SaveChangesAsync();
        user.BranchId = branch.Id;

        var session = new AcademicSession
        {
            SchoolId = school.Id, BranchId = branch.Id, Name = "2026-27",
            StartDate = new DateOnly(2026, 4, 1), EndDate = new DateOnly(2027, 3, 31), IsCurrent = true
        };
        var klass = new SchoolClass { SchoolId = school.Id, BranchId = branch.Id, Name = "Class 1", Code = "C1" };
        db.AddRange(session, klass);
        await db.SaveChangesAsync();

        var section = new Section { SchoolId = school.Id, BranchId = branch.Id, ClassId = klass.Id, Name = "A", Capacity = 30 };
        db.Add(section);
        await db.SaveChangesAsync();

        var student = new Student
        {
            SchoolId = school.Id, BranchId = branch.Id, AdmissionNo = "ADM-1",
            AcademicSessionId = session.Id, ClassId = klass.Id, SectionId = section.Id,
            FirstName = "Test", LastName = "Student", DateOfBirth = new DateOnly(2019, 1, 1),
            Gender = Gender.Male, AdmissionDate = session.StartDate, Status = StudentStatus.Active
        };
        var category = new FeeCategory { SchoolId = school.Id, BranchId = branch.Id, Name = "Tuition", Code = "TUI" };
        db.AddRange(student, category);
        await db.SaveChangesAsync();

        var structure = new FeeStructure
        {
            SchoolId = school.Id, BranchId = branch.Id, AcademicSessionId = session.Id,
            ClassId = klass.Id, Name = "Class 1 fees", TotalAmount = 1000m
        };
        db.Add(structure);
        await db.SaveChangesAsync();

        db.Add(new FeeStructureItem
        {
            SchoolId = school.Id, BranchId = branch.Id, FeeStructureId = structure.Id,
            FeeCategoryId = category.Id, Amount = 1000m, Frequency = FeeFrequency.Annual
        });

        var assignment = new StudentFeeAssignment
        {
            SchoolId = school.Id, BranchId = branch.Id, StudentId = student.Id,
            FeeStructureId = structure.Id, AcademicSessionId = session.Id,
            TotalAmount = 1000m, NetAmount = 1000m, IsActive = true
        };
        db.Add(assignment);
        await db.SaveChangesAsync();

        var installment = new FeeInstallment
        {
            SchoolId = school.Id, BranchId = branch.Id, StudentFeeAssignmentId = assignment.Id,
            Name = "Term 1", DueDate = clock.Today.AddDays(10), Amount = 1000m,
            Status = InstallmentStatus.Pending
        };
        db.Add(installment);
        await db.SaveChangesAsync();

        return (student, installment);
    }

    private sealed class FakeGateway : IPaymentGateway
    {
        public string Name => "Fake";
        public bool IsEnabled => true;

        public Task<GatewayOrder> CreateOrderAsync(decimal amount, string reference,
            CancellationToken ct = default) =>
            Task.FromResult(new GatewayOrder(
                $"FAKE-ORD-{amount:0.00}-{Guid.NewGuid():N}",
                $"FAKE-PAY-{Guid.NewGuid():N}",
                amount, "INR", "/checkout", DateTime.UtcNow.AddMinutes(30)));

        public Task<GatewayVerification> VerifyAsync(string orderId, string paymentId, decimal amount,
            CancellationToken ct = default) =>
            Task.FromResult(new GatewayVerification(true, paymentId, null, "ok"));

        public Task<GatewayVerification> RefundAsync(string paymentId, decimal amount,
            CancellationToken ct = default) =>
            Task.FromResult(new GatewayVerification(true, paymentId, null, "ok"));
    }
}
