using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Application.Homework;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Infrastructure.Persistence;
using Xunit;

namespace SchoolErp.Tests;

/// <summary>
/// A teacher must be able to create homework whenever they have a genuine teaching
/// relationship with the class/section. The previous exact (class, section) check
/// rejected class teachers, subject teachers and teachers assigned to a sibling section.
/// </summary>
public sealed class HomeworkTeacherAssignmentTests
{
    private sealed record Fixture(
        TestClock Clock, TestCurrentUser User, AppDbContext Db,
        SchoolClass Class, Section SectionA, Section SectionB, Subject Subject, Employee Teacher) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    [Fact]
    public async Task Create_ExactSectionAssignment_AttributesHomeworkToTeacher()
    {
        await using var f = await SeedAsync(RoleCodes.Teacher);
        f.Db.Add(new TeacherAssignment
        {
            SchoolId = f.User.SchoolId!.Value, EmployeeId = f.Teacher.Id,
            ClassId = f.Class.Id, SectionId = f.SectionA.Id
        });
        await f.Db.SaveChangesAsync();

        var result = await CreateHomeworkAsync(f, f.SectionA.Id);

        Assert.Equal(f.Teacher.Id, result.EmployeeId);
    }

    [Fact]
    public async Task Create_ClassTeacherAssignmentWithoutExactRow_AttributesHomeworkToTeacher()
    {
        await using var f = await SeedAsync(RoleCodes.Teacher);
        f.SectionA.ClassTeacherId = f.Teacher.Id;
        await f.Db.SaveChangesAsync();

        var result = await CreateHomeworkAsync(f, f.SectionA.Id);

        Assert.Equal(f.Teacher.Id, result.EmployeeId);
    }

    [Fact]
    public async Task Create_SubjectTeacherAssignment_AttributesHomeworkToTeacher()
    {
        await using var f = await SeedAsync(RoleCodes.Teacher);
        f.Db.Add(new ClassSubject
        {
            SchoolId = f.User.SchoolId!.Value, ClassId = f.Class.Id,
            SubjectId = f.Subject.Id, TeacherId = f.Teacher.Id
        });
        await f.Db.SaveChangesAsync();

        var result = await CreateHomeworkAsync(f, f.SectionA.Id);

        Assert.Equal(f.Teacher.Id, result.EmployeeId);
    }

    [Fact]
    public async Task Create_AssignedToSiblingSectionOfSameClass_AttributesHomeworkToTeacher()
    {
        await using var f = await SeedAsync(RoleCodes.Teacher);
        f.Db.Add(new TeacherAssignment
        {
            SchoolId = f.User.SchoolId!.Value, EmployeeId = f.Teacher.Id,
            ClassId = f.Class.Id, SectionId = f.SectionB.Id
        });
        await f.Db.SaveChangesAsync();

        var result = await CreateHomeworkAsync(f, f.SectionA.Id);

        Assert.Equal(f.Teacher.Id, result.EmployeeId);
    }

    [Fact]
    public async Task Create_DeletedAssignment_DoesNotAuthorize()
    {
        await using var f = await SeedAsync(RoleCodes.Teacher);
        f.Db.Add(new TeacherAssignment
        {
            SchoolId = f.User.SchoolId!.Value, EmployeeId = f.Teacher.Id,
            ClassId = f.Class.Id, SectionId = f.SectionA.Id, IsDeleted = true
        });
        await f.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<DomainException>(() => CreateHomeworkAsync(f, f.SectionA.Id));

        Assert.Equal("forbidden", error.Code);
        Assert.Contains("not assigned", error.Message);
    }

    [Fact]
    public async Task Create_AssignmentInAnotherSchool_DoesNotAuthorize()
    {
        await using var f = await SeedAsync(RoleCodes.Teacher);
        f.Db.Add(new TeacherAssignment
        {
            SchoolId = f.User.SchoolId!.Value + 100, EmployeeId = f.Teacher.Id,
            ClassId = f.Class.Id, SectionId = f.SectionA.Id
        });
        await f.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<DomainException>(() => CreateHomeworkAsync(f, f.SectionA.Id));

        Assert.Equal("forbidden", error.Code);
    }

    [Fact]
    public async Task Create_CompletelyUnassignedTeacher_IsStillForbidden()
    {
        await using var f = await SeedAsync(RoleCodes.Teacher);

        var error = await Assert.ThrowsAsync<DomainException>(() => CreateHomeworkAsync(f, f.SectionA.Id));

        Assert.Equal("forbidden", error.Code);
        Assert.Contains("not assigned", error.Message);
    }

    [Theory]
    [InlineData(RoleCodes.Principal)]
    [InlineData(RoleCodes.Administrator)]
    [InlineData(RoleCodes.SchoolOwner)]
    [InlineData(RoleCodes.SuperAdmin)]
    public async Task Create_PrivilegedRoleWithEmployeeId_DoesNotNeedAssignment(string role)
    {
        await using var f = await SeedAsync(role);

        var result = await CreateHomeworkAsync(f, f.SectionA.Id);

        Assert.Equal(f.Teacher.Id, result.EmployeeId);
    }

    private static async Task<HomeworkDto> CreateHomeworkAsync(Fixture f, int sectionId)
    {
        var service = new HomeworkService(f.Db, new UnitOfWork(f.Db), f.User, f.Clock);
        return await service.CreateAsync(new SaveHomeworkRequest
        {
            ClassId = f.Class.Id,
            SectionId = sectionId,
            SubjectId = f.Subject.Id,
            Title = "Fractions",
            DueAtUtc = f.Clock.UtcNow.AddDays(2),
            IsPublished = true
        });
    }

    private static async Task<Fixture> SeedAsync(string role)
    {
        var clock = new TestClock();
        var user = new TestCurrentUser
        {
            Roles = new[] { role },
            Permissions = new[] { Permissions.HomeworkManage }
        };
        var db = TestDb.Create(user, clock);

        var school = new School { Code = "HW", Name = "Homework Test School" };
        db.Add(school);
        await db.SaveChangesAsync();
        user.SchoolId = school.Id;

        var klass = new SchoolClass { SchoolId = school.Id, Name = "Class 5", Code = "C5" };
        var subject = new Subject { SchoolId = school.Id, Name = "Mathematics", Code = "MATH" };
        var teacher = new Employee
        {
            SchoolId = school.Id, EmployeeNo = "EMP-1", FirstName = "Tina", LastName = "Teacher",
            Email = "tina@school.test", JoiningDate = new DateOnly(2025, 1, 1)
        };
        db.AddRange(klass, subject, teacher);
        await db.SaveChangesAsync();
        user.EmployeeId = teacher.Id;

        var sectionA = new Section { SchoolId = school.Id, ClassId = klass.Id, Name = "A", Capacity = 40 };
        var sectionB = new Section { SchoolId = school.Id, ClassId = klass.Id, Name = "B", Capacity = 40 };
        db.AddRange(sectionA, sectionB);
        await db.SaveChangesAsync();

        return new Fixture(clock, user, db, klass, sectionA, sectionB, subject, teacher);
    }
}
