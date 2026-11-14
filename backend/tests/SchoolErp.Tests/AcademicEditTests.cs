using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Administration;
using SchoolErp.Application.Common;
using SchoolErp.Application.Homework;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
using SchoolErp.Infrastructure.Persistence;
using Xunit;

namespace SchoolErp.Tests;

public sealed class AcademicEditTests
{
    [Fact]
    public async Task DeleteHomework_WithGradedSubmission_IsBlocked()
    {
        var clock = new TestClock();
        var user = PrincipalUser();
        await using var db = TestDb.Create(user, clock);
        var (klass, section, subject) = await SeedAcademicsAsync(db, user);

        var homework = new Homework
        {
            SchoolId = user.SchoolId!.Value, ClassId = klass.Id, SectionId = section.Id, SubjectId = subject.Id,
            EmployeeId = 1, Title = "Fractions", DueAtUtc = clock.UtcNow.AddDays(2), MaxMarks = 10
        };
        db.Add(homework);
        await db.SaveChangesAsync();
        db.Add(new AssignmentSubmission
        {
            SchoolId = user.SchoolId!.Value, HomeworkId = homework.Id, StudentId = 1, Marks = 8
        });
        await db.SaveChangesAsync();

        var service = new HomeworkService(db, new UnitOfWork(db), user, clock);

        var error = await Assert.ThrowsAsync<DomainException>(() => service.DeleteAsync(homework.Id));
        Assert.Equal("conflict", error.Code);
        Assert.False(await db.Homework.AsNoTracking().Where(h => h.Id == homework.Id).Select(h => h.IsDeleted).SingleAsync());
    }

    [Fact]
    public async Task DeleteSubject_InUse_IsBlocked()
    {
        var clock = new TestClock();
        var user = PrincipalUser();
        await using var db = TestDb.Create(user, clock);
        var (klass, _, subject) = await SeedAcademicsAsync(db, user);
        db.Add(new ClassSubject { SchoolId = user.SchoolId!.Value, ClassId = klass.Id, SubjectId = subject.Id });
        var unused = new Subject { SchoolId = user.SchoolId!.Value, Name = "Art", Code = "ART" };
        db.Add(unused);
        await db.SaveChangesAsync();

        var service = new AdministrationService(db, new UnitOfWork(db), user, clock);

        var error = await Assert.ThrowsAsync<DomainException>(() => service.DeleteSubjectAsync(subject.Id));
        Assert.Equal("conflict", error.Code);
        Assert.Contains("class subjects", error.Message);

        await service.DeleteSubjectAsync(unused.Id);
        var remaining = await service.GetSubjectsAsync();
        Assert.DoesNotContain(remaining, s => s.Id == unused.Id);
    }

    [Fact]
    public async Task DeleteSection_WithStudents_IsBlocked()
    {
        var clock = new TestClock();
        var user = PrincipalUser();
        await using var db = TestDb.Create(user, clock);
        var (klass, section, _) = await SeedAcademicsAsync(db, user);
        db.Add(new Student
        {
            SchoolId = user.SchoolId!.Value, AdmissionNo = "ADM-1", ClassId = klass.Id, SectionId = section.Id,
            FirstName = "Ravi", LastName = "Kumar", DateOfBirth = new DateOnly(2015, 4, 1),
            AdmissionDate = new DateOnly(2025, 4, 1), Status = StudentStatus.Active
        });
        await db.SaveChangesAsync();

        var service = new AdministrationService(db, new UnitOfWork(db), user, clock);

        var error = await Assert.ThrowsAsync<DomainException>(() => service.DeleteSectionAsync(klass.Id, section.Id));
        Assert.Equal("conflict", error.Code);
        var classError = await Assert.ThrowsAsync<DomainException>(() => service.DeleteClassAsync(klass.Id));
        Assert.Equal("conflict", classError.Code);
    }

    private static TestCurrentUser PrincipalUser() => new()
    {
        Roles = new[] { RoleCodes.Principal },
        Permissions = new[] { "settings.manage", "homework.manage" }
    };

    private static async Task<(SchoolClass, Section, Subject)> SeedAcademicsAsync(AppDbContext db, TestCurrentUser user)
    {
        var school = new School { Code = "TEST", Name = "Test School" };
        db.Add(school);
        await db.SaveChangesAsync();
        user.SchoolId = school.Id;

        var klass = new SchoolClass { SchoolId = school.Id, Name = "Class 5", Code = "C5" };
        var subject = new Subject { SchoolId = school.Id, Name = "Mathematics", Code = "MATH" };
        db.AddRange(klass, subject);
        await db.SaveChangesAsync();

        var section = new Section { SchoolId = school.Id, ClassId = klass.Id, Name = "A", Capacity = 40 };
        db.Add(section);
        await db.SaveChangesAsync();
        return (klass, section, subject);
    }
}
