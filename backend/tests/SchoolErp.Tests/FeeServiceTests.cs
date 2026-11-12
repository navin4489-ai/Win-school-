using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Application.Fees;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
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

        Assert.StartsWith("RCPT-DEMO-", first.ReceiptNo);
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

    private static async Task<(Student Student, FeeInstallment Installment)> SeedAsync(AppDbContext db,
        TestCurrentUser user, TestClock clock)
    {
        var school = new School { Code = "DEMO", Name = "Demo School" };
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
        public Task<GatewayVerification> VerifyAsync(string orderId, string paymentId, decimal amount,
            CancellationToken ct = default) =>
            Task.FromResult(new GatewayVerification(true, paymentId, null, "ok"));

        public Task<GatewayVerification> RefundAsync(string paymentId, decimal amount,
            CancellationToken ct = default) =>
            Task.FromResult(new GatewayVerification(true, paymentId, null, "ok"));
    }
}
