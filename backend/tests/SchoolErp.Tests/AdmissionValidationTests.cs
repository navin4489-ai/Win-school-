using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Admissions;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
using SchoolErp.Infrastructure.Identity;
using SchoolErp.Infrastructure.Persistence;
using Xunit;

namespace SchoolErp.Tests;

public sealed class AdmissionValidationTests
{
    [Fact]
    public void BlankGuardianEmail_IsValid_ButInvalidAddressIsRejected()
    {
        Assert.True(IsValid(""));
        Assert.True(IsValid("   "));
        Assert.True(IsValid(null));
        Assert.True(IsValid("parent@example.test"));

        var errors = Validate("notanemail");
        Assert.False(errors.Count == 0);
        Assert.Contains(errors, e => e.ErrorMessage!.Contains("not a valid e-mail address"));
    }

    [Fact]
    public async Task CreateEnquiry_WithBlankGuardianEmail_SucceedsAndStoresNull()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser { Roles = new[] { RoleCodes.AdmissionManager } };
        await using var db = TestDb.Create(user, clock);

        var school = new School { Code = "TEST", Name = "Test School" };
        db.Add(school);
        await db.SaveChangesAsync();
        user.SchoolId = school.Id;

        db.AddRange(
            new AcademicSession
            {
                SchoolId = school.Id, Name = "2026-27", StartDate = new DateOnly(2026, 4, 1),
                EndDate = new DateOnly(2027, 3, 31), IsCurrent = true
            },
            new SchoolClass { SchoolId = school.Id, Name = "Class 1", Code = "C1" });
        await db.SaveChangesAsync();

        var service = new AdmissionService(db, new UnitOfWork(db), user, new PasswordHasherService(), clock);
        var request = new CreateEnquiryRequest
        {
            FirstName = "Asha", LastName = "Rao", DateOfBirth = new DateOnly(2016, 5, 1),
            Gender = Gender.Female, GuardianName = "Parent Rao", GuardianPhone = "9999999999",
            GuardianEmail = "", ClassId = 1, SessionId = 1
        };

        var created = await service.CreateEnquiryAsync(request);

        Assert.Null(created.GuardianEmail);
        Assert.Null(await db.Admissions.Select(a => a.GuardianEmail).SingleAsync());
    }

    private static bool IsValid(string? email) => Validate(email).Count == 0;

    private static List<ValidationResult> Validate(string? email)
    {
        var request = new CreateEnquiryRequest { GuardianEmail = email };
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results.Where(r => r.MemberNames.Contains(nameof(CreateEnquiryRequest.GuardianEmail))).ToList();
    }
}
