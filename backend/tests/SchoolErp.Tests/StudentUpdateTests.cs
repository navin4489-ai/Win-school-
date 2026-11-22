using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Application.Students;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
using SchoolErp.Infrastructure.Persistence;
using Xunit;

namespace SchoolErp.Tests;

public sealed class StudentUpdateTests
{
    [Fact]
    public async Task Update_WithoutHiddenFields_KeepsPhotoAndMedicalNotes()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser { Permissions = new[] { Permissions.StudentsWrite } };
        await using var db = TestDb.Create(user, clock);

        var school = new School { Code = "TEST", Name = "Test School" };
        db.Add(school);
        await db.SaveChangesAsync();
        user.SchoolId = school.Id;

        var student = new Student
        {
            SchoolId = school.Id, AdmissionNo = "A1001", AcademicSessionId = 1,
            ClassId = 1, SectionId = 1, FirstName = "Asha", LastName = "Rao",
            DateOfBirth = new DateOnly(2016, 5, 1), Gender = Gender.Female,
            AdmissionDate = new DateOnly(2025, 4, 1), Status = StudentStatus.Active,
            PhotoUrl = "/files/student-photo.jpg", MedicalNotes = "Asthma"
        };
        db.Add(student);
        await db.SaveChangesAsync();

        var service = new StudentService(db, new UnitOfWork(db), user, new NoOpFileStorage(), clock);
        var updated = await service.UpdateAsync(student.Id, new UpdateStudentRequest
        {
            FirstName = "Asha", LastName = "Rao", DateOfBirth = new DateOnly(2016, 5, 1),
            Gender = Gender.Female, Phone = "9999999999", Email = ""
        });

        Assert.Equal("9999999999", updated.Phone);
        Assert.Equal("Asthma", await db.Students.Select(s => s.MedicalNotes).SingleAsync());
        Assert.Equal("/files/student-photo.jpg", await db.Students.Select(s => s.PhotoUrl).SingleAsync());
    }

    private sealed class NoOpFileStorage : IFileStorage
    {
        public Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default) =>
            Task.FromResult(new StoredFile(fileName, null));

        public Task DeleteAsync(string path, CancellationToken ct = default) => Task.CompletedTask;

        public string? GetDownloadUrl(string path, TimeSpan lifetime) => null;
    }
}
