using Microsoft.EntityFrameworkCore;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
using Xunit;

namespace SchoolErp.Tests;

public sealed class TenantFilterTests
{
    [Fact]
    public async Task StudentsQuery_IsScopedToTheCurrentSchool()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser();
        await using var db = TestDb.Create(user, clock);

        var schoolA = new School { Code = "A", Name = "School A" };
        var schoolB = new School { Code = "B", Name = "School B" };
        db.AddRange(schoolA, schoolB);
        await db.SaveChangesAsync();

        db.AddRange(
            new Student
            {
                SchoolId = schoolA.Id, AdmissionNo = "A-1", AcademicSessionId = 1, ClassId = 1, SectionId = 1,
                FirstName = "A", LastName = "Student", DateOfBirth = new DateOnly(2019, 1, 1),
                Gender = Gender.Male, AdmissionDate = new DateOnly(2026, 4, 1)
            },
            new Student
            {
                SchoolId = schoolB.Id, AdmissionNo = "B-1", AcademicSessionId = 1, ClassId = 1, SectionId = 1,
                FirstName = "B", LastName = "Student", DateOfBirth = new DateOnly(2019, 1, 1),
                Gender = Gender.Female, AdmissionDate = new DateOnly(2026, 4, 1)
            });
        await db.SaveChangesAsync();

        user.SchoolId = schoolA.Id;
        var visible = await db.Students.AsNoTracking().ToListAsync();

        Assert.Single(visible);
        Assert.Equal("A-1", visible[0].AdmissionNo);

        user.SchoolId = schoolB.Id;
        var other = await db.Students.AsNoTracking().ToListAsync();
        Assert.Single(other);
        Assert.Equal("B-1", other[0].AdmissionNo);

        var all = await db.Students.IgnoreQueryFilters().AsNoTracking().CountAsync();
        Assert.Equal(2, all);
    }
}
