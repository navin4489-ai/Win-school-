using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolErp.Application.Auth;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Infrastructure.Identity;
using SchoolErp.Infrastructure.Persistence;
using Xunit;

namespace SchoolErp.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task Login_ReturnsTokensAndPermissions()
    {
        var (db, user, clock) = await SeedAsync();
        var service = CreateService(db, user, clock);

        var response = await service.LoginAsync(new LoginRequest
        {
            Email = "accountant@demo.edu",
            Password = "Passw0rd!",
            SchoolCode = "DEMO"
        });

        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(response.RefreshToken));
        Assert.Equal("accountant@demo.edu", response.User.Email);
        Assert.Equal(RoleCodes.Accountant, response.User.RoleCode);
        Assert.Contains(Permissions.FeesCollect, response.User.Permissions);
        Assert.True(response.ExpiresAtUtc > clock.UtcNow);
    }

    [Fact]
    public async Task Login_LocksAccountAfterFiveFailures()
    {
        var (db, user, clock) = await SeedAsync();
        var service = CreateService(db, user, clock);

        for (var i = 0; i < 5; i++)
        {
            var failure = await Assert.ThrowsAsync<DomainException>(() => service.LoginAsync(new LoginRequest
            {
                Email = "accountant@demo.edu",
                Password = "WrongPassword1!"
            }));
            Assert.Equal("unauthorized", failure.Code);
        }

        var locked = await Assert.ThrowsAsync<DomainException>(() => service.LoginAsync(new LoginRequest
        {
            Email = "accountant@demo.edu",
            Password = "Passw0rd!"
        }));

        Assert.Equal("account_locked", locked.Code);
        Assert.Equal(423, locked.StatusCode);
    }

    private static AuthService CreateService(AppDbContext db, TestCurrentUser user, TestClock clock)
    {
        var hasher = new PasswordHasherService();
        var tokens = new TokenService(Options.Create(new JwtOptions
        {
            Issuer = "test",
            Audience = "test-clients",
            SigningKey = new string('k', 64),
            AccessTokenMinutes = 30
        }), clock);

        return new AuthService(db, hasher, tokens, user, new UnitOfWork(db), clock);
    }

    private static async Task<(AppDbContext Db, TestCurrentUser User, TestClock Clock)> SeedAsync()
    {
        var clock = new TestClock();
        var user = new TestCurrentUser { UserId = null, Permissions = Array.Empty<string>() };
        var db = TestDb.Create(user, clock);

        var school = new School { Code = "DEMO", Name = "Demo School" };
        db.Add(school);
        await db.SaveChangesAsync();
        user.SchoolId = school.Id;

        var role = new Role
        {
            SchoolId = school.Id, Name = "Accountant", Code = RoleCodes.Accountant, IsSystem = true
        };
        var permission = new Permission
        {
            Code = Permissions.FeesCollect, Module = "Fees", Action = "Collect"
        };
        db.AddRange(role, permission);
        await db.SaveChangesAsync();
        db.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });

        var hasher = new PasswordHasherService();
        var account = new UserAccount
        {
            SchoolId = school.Id, Email = "accountant@demo.edu", NormalizedEmail = "accountant@demo.edu",
            FullName = "Demo Accountant", PasswordHash = hasher.Hash("Passw0rd!"),
            RoleId = role.Id, IsActive = true, EmailConfirmed = true
        };
        db.Add(account);
        await db.SaveChangesAsync();

        return (db, user, clock);
    }
}
