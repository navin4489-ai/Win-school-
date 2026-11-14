using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Infrastructure.Persistence;
using SchoolErp.Infrastructure.Services;

namespace SchoolErp.Tests;

internal sealed class TestClock : IDateTimeProvider
{
    public TestClock(DateTime? utcNow = null) => UtcNow = utcNow ?? new DateTime(2026, 11, 12, 6, 0, 0, DateTimeKind.Utc);

    public DateTime UtcNow { get; set; }
    public DateOnly Today => DateOnly.FromDateTime(UtcNow);
}

internal sealed class TestCurrentUser : ICurrentUser
{
    public int? UserId { get; set; } = 1;
    public int? SchoolId { get; set; }
    public int? BranchId { get; set; }
    public int? EmployeeId { get; set; }
    public int? GuardianId { get; set; }
    public int? StudentId { get; set; }
    public string? Email { get; set; } = "tester@school.test";
    public string? IpAddress { get; set; } = "127.0.0.1";
    public string? UserAgent { get; set; } = "xunit";
    public bool IsAuthenticated => UserId is not null;
    public bool IsSystemContext => SchoolId is null;
    public IReadOnlyCollection<string> Permissions { get; set; } = Array.Empty<string>();
    public IReadOnlyCollection<string> Roles { get; set; } = new[] { RoleCodes.Accountant };

    public bool HasPermission(string permission) => Permissions.Contains(permission);

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

internal static class TestDb
{
    public static AppDbContext Create(TestCurrentUser user, TestClock clock)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"school-erp-tests-{Guid.NewGuid():N}")
            .EnableSensitiveDataLogging()
            .Options;

        var db = new AppDbContext(options, user, clock, new NullNotificationPublisher());
        db.Database.EnsureCreated();
        return db;
    }
}
