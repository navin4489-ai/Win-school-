using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Application.Exams;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
using SchoolErp.Infrastructure.Persistence;
using Xunit;

namespace SchoolErp.Tests;

public sealed class ExamMarksAccessTests
{
    [Fact]
    public async Task Parent_GetMarks_SeesOnlyOwnChild_TeacherSeesWholeClass()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser { Permissions = new[] { Permissions.ExamsRead } };
        await using var db = TestDb.Create(user, clock);
        var (examSubjectId, ownChildId, otherChildId, guardianId) = await SeedAsync(db, user);

        user.Roles = new[] { RoleCodes.Parent };
        user.GuardianId = guardianId;
        var parentMarks = await new ExamService(db, new UnitOfWork(db), user, clock).GetMarksAsync(examSubjectId);

        Assert.Equal(new[] { ownChildId }, parentMarks.Select(m => m.StudentId));
        Assert.All(parentMarks, m => Assert.Equal("Parent Child", m.StudentName));

        user.Roles = new[] { RoleCodes.Teacher };
        user.GuardianId = null;
        var teacherMarks = await new ExamService(db, new UnitOfWork(db), user, clock).GetMarksAsync(examSubjectId);

        Assert.Equal(2, teacherMarks.Count);
        Assert.Contains(teacherMarks, m => m.StudentId == ownChildId);
        Assert.Contains(teacherMarks, m => m.StudentId == otherChildId);
    }

    private static async Task<(int ExamSubjectId, int OwnChildId, int OtherChildId, int GuardianId)> SeedAsync(
        AppDbContext db, TestCurrentUser user)
    {
        var school = new School { Code = "TEST", Name = "Test School" };
        db.Add(school);
        await db.SaveChangesAsync();
        user.SchoolId = school.Id;

        var klass = new SchoolClass { SchoolId = school.Id, Name = "Class 6", Code = "C6" };
        var section = new Section { SchoolId = school.Id, ClassId = 0, Name = "A", Capacity = 40 };
        var subject = new Subject { SchoolId = school.Id, Name = "Maths", Code = "MATH" };
        var exam = new Examination
        {
            SchoolId = school.Id, AcademicSessionId = 1, Name = "Term 1", ExamType = ExamType.UnitTest,
            StartDate = new DateOnly(2026, 11, 1), EndDate = new DateOnly(2026, 11, 2),
            Status = ExamStatus.Published
        };
        db.AddRange(klass, section, subject, exam);
        await db.SaveChangesAsync();
        section.ClassId = klass.Id;
        await db.SaveChangesAsync();

        var examSubject = new ExamSubject
        {
            SchoolId = school.Id, ExaminationId = exam.Id, ClassId = klass.Id, SubjectId = subject.Id
        };
        db.Add(examSubject);

        var ownChild = new Student
        {
            SchoolId = school.Id, AdmissionNo = "A1001", AcademicSessionId = 1,
            ClassId = klass.Id, SectionId = section.Id, FirstName = "Parent", LastName = "Child",
            DateOfBirth = new DateOnly(2015, 1, 1), Gender = Gender.Male,
            AdmissionDate = new DateOnly(2025, 4, 1), Status = StudentStatus.Active
        };
        var otherChild = new Student
        {
            SchoolId = school.Id, AdmissionNo = "A1002", AcademicSessionId = 1,
            ClassId = klass.Id, SectionId = section.Id, FirstName = "Other", LastName = "Child",
            DateOfBirth = new DateOnly(2015, 2, 1), Gender = Gender.Female,
            AdmissionDate = new DateOnly(2025, 4, 1), Status = StudentStatus.Active
        };
        db.AddRange(ownChild, otherChild);
        await db.SaveChangesAsync();

        var guardian = new Guardian
        {
            SchoolId = school.Id, FirstName = "Parent", LastName = "Guardian", Phone = "9999999999"
        };
        db.Add(guardian);
        await db.SaveChangesAsync();
        db.Add(new StudentParent { SchoolId = school.Id, StudentId = ownChild.Id, GuardianId = guardian.Id, IsPrimary = true });

        db.AddRange(
            new StudentMark
            {
                SchoolId = school.Id, ExaminationId = exam.Id, ExamSubjectId = examSubject.Id,
                StudentId = ownChild.Id, TheoryMarks = 88
            },
            new StudentMark
            {
                SchoolId = school.Id, ExaminationId = exam.Id, ExamSubjectId = examSubject.Id,
                StudentId = otherChild.Id, TheoryMarks = 42
            });
        await db.SaveChangesAsync();

        return (examSubject.Id, ownChild.Id, otherChild.Id, guardian.Id);
    }
}
