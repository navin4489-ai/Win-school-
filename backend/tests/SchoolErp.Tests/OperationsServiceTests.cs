using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolErp.Application.Operations;
using SchoolErp.Application.Reports;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
using SchoolErp.Infrastructure.Persistence;
using Xunit;

namespace SchoolErp.Tests;

public sealed class OperationsServiceTests
{
    [Fact]
    public async Task RecordStock_AdjustmentToZero_StoresDifference()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser();
        await using var db = TestDb.Create(user, clock);
        await SeedSchoolAsync(db, user);
        var item = new InventoryItem
        {
            SchoolId = user.SchoolId!.Value, Name = "Chalk", Code = "CHK", Unit = "Box", Quantity = 12m
        };
        db.Add(item);
        await db.SaveChangesAsync();
        var service = CreateService(db, user, clock);

        var result = await service.RecordStockAsync(new StockEntryRequest
        {
            InventoryItemId = item.Id, Type = StockTransactionType.Adjustment, Quantity = 0m
        });

        Assert.Equal(0m, result.Quantity);
        var transaction = await db.StockTransactions.AsNoTracking().SingleAsync(t => t.InventoryItemId == item.Id);
        Assert.Equal(-12m, transaction.Quantity);

        var error = await Assert.ThrowsAsync<DomainException>(() => service.RecordStockAsync(new StockEntryRequest
        {
            InventoryItemId = item.Id, Type = StockTransactionType.Outward, Quantity = 1m
        }));
        Assert.Equal("validation_error", error.Code);

        var zeroInward = await Assert.ThrowsAsync<DomainException>(() => service.RecordStockAsync(new StockEntryRequest
        {
            InventoryItemId = item.Id, Type = StockTransactionType.Inward, Quantity = 0m
        }));
        Assert.Equal("validation_error", zeroInward.Code);
    }

    [Fact]
    public async Task ApplyLeave_RejectsOverlappingPendingOrApprovedLeave()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser();
        await using var db = TestDb.Create(user, clock);
        await SeedSchoolAsync(db, user);
        var employee = new Employee
        {
            SchoolId = user.SchoolId!.Value, EmployeeNo = "EMP-1", FirstName = "Asha", LastName = "Rao",
            Email = "asha@school.test", JoiningDate = new DateOnly(2025, 1, 1), Status = EmploymentStatus.Active
        };
        db.Add(employee);
        await db.SaveChangesAsync();
        user.EmployeeId = employee.Id;
        var service = CreateService(db, user, clock);

        await service.ApplyLeaveAsync(new ApplyLeaveRequest
        {
            FromDate = new DateOnly(2026, 12, 1), ToDate = new DateOnly(2026, 12, 5), Reason = "Travel"
        });

        var error = await Assert.ThrowsAsync<DomainException>(() => service.ApplyLeaveAsync(new ApplyLeaveRequest
        {
            FromDate = new DateOnly(2026, 12, 5), ToDate = new DateOnly(2026, 12, 7), Reason = "Again"
        }));
        Assert.Equal("conflict", error.Code);

        var adjacent = await service.ApplyLeaveAsync(new ApplyLeaveRequest
        {
            FromDate = new DateOnly(2026, 12, 6), ToDate = new DateOnly(2026, 12, 7), Reason = "Adjacent"
        });
        Assert.Equal(LeaveStatus.Pending, adjacent.Status);

        var selfApprove = await Assert.ThrowsAsync<DomainException>(() =>
            service.ApproveLeaveAsync(adjacent.Id, true, null));
        Assert.Equal("forbidden", selfApprove.Code);
    }

    [Theory]
    [InlineData("=SUM(A1:A2)", "'=SUM(A1:A2)")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@HYPERLINK(\"x\")", "\"'@HYPERLINK(\"\"x\"\")\"")]
    [InlineData("\tdata", "'\tdata")]
    [InlineData("\rdata", "\"'\rdata\"")]
    [InlineData("Plain text", "Plain text")]
    [InlineData("a,b", "\"a,b\"")]
    public void Csv_EscapesFormulaInjection(string input, string expected)
    {
        Assert.Equal(expected, ReportService.Csv(input));
    }

    private static OperationsService CreateService(AppDbContext db, TestCurrentUser user, TestClock clock) =>
        new(db, new UnitOfWork(db), user, clock, Options.Create(new LibraryOptions()));

    private static async Task SeedSchoolAsync(AppDbContext db, TestCurrentUser user)
    {
        var school = new School { Code = "TEST", Name = "Test School" };
        db.Add(school);
        await db.SaveChangesAsync();
        user.SchoolId = school.Id;
    }
}
