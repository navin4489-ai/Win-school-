using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
using SchoolErp.Infrastructure.Identity;

namespace SchoolErp.Infrastructure.Persistence;

/// <summary>
/// Creates the platform school and first super admin on first start (from configuration)
/// and repairs staff accounts that are missing an employee record.
/// </summary>
public sealed class DbInitializer
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<DbInitializer> _logger;

    public DbInitializer(AppDbContext db, IPasswordHasher hasher, IDateTimeProvider clock,
        ILogger<DbInitializer> logger)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Staff accounts created before user management linked employees have no employee
    /// record, so they were missing from teacher lists. Creates and links one per account.
    /// </summary>
    private async Task LinkStaffEmployeesAsync(CancellationToken ct)
    {
        var nonStaff = new[] { RoleCodes.Parent, RoleCodes.Student, RoleCodes.SuperAdmin };
        var orphans = await (
            from u in _db.UserAccounts
            join r in _db.Roles on u.RoleId equals r.Id
            where u.EmployeeId == null && !nonStaff.Contains(r.Code)
            select u).ToListAsync(ct);

        foreach (var user in orphans)
        {
            var employee = await _db.Employees
                .FirstOrDefaultAsync(e => e.SchoolId == user.SchoolId && e.Email == user.Email, ct);
            if (employee is null)
            {
                var count = await _db.Employees.CountAsync(e => e.SchoolId == user.SchoolId, ct);
                var parts = user.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                employee = new Employee
                {
                    SchoolId = user.SchoolId, BranchId = user.BranchId,
                    EmployeeNo = $"EMP-{_clock.Today.Year}-{count + 1:0000}-{user.Id}",
                    FirstName = parts.ElementAtOrDefault(0) ?? user.Email,
                    LastName = parts.ElementAtOrDefault(1) ?? string.Empty,
                    Email = user.Email, Phone = user.Phone, JoiningDate = _clock.Today
                };
                _db.Add(employee);
                await _db.SaveChangesAsync(ct);
            }

            user.EmployeeId = employee.Id;
        }

        if (orphans.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Linked {Count} staff accounts to employee records.", orphans.Count);
        }
    }

    /// <summary>
    /// Production first start: creates the platform school and one super admin from
    /// configuration, and nothing else. No demo data and no shared passwords.
    /// Does nothing once any school exists.
    /// </summary>
    public async Task BootstrapPlatformAsync(string schoolCode, string schoolName, string adminEmail,
        string adminPassword, CancellationToken ct = default)
    {
        if (await _db.Schools.AnyAsync(ct))
        {
            await LinkStaffEmployeesAsync(ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 12)
            throw new InvalidOperationException(
                "First start needs Bootstrap:AdminEmail and Bootstrap:AdminPassword (at least 12 characters).");

        _logger.LogInformation("Creating platform school {Code} and super admin {Email}.", schoolCode, adminEmail);
        var today = _clock.Today;
        var startYear = today.Month >= 4 ? today.Year : today.Year - 1;

        var school = new School { Code = schoolCode.Trim().ToUpperInvariant(), Name = schoolName.Trim(), IsActive = true };
        _db.Add(school);
        await _db.SaveChangesAsync(ct);

        var branch = new Branch { SchoolId = school.Id, Name = "Head office", Code = "MAIN", IsMainBranch = true };
        _db.Add(branch);
        await _db.SaveChangesAsync(ct);

        _db.Add(new AcademicSession
        {
            SchoolId = school.Id, BranchId = branch.Id, Name = $"{startYear}-{(startYear + 1) % 100:00}",
            StartDate = new DateOnly(startYear, 4, 1), EndDate = new DateOnly(startYear + 1, 3, 31), IsCurrent = true
        });

        var permissions = await _db.Permissions.ToListAsync(ct);
        foreach (var p in Permissions.All.Where(p => permissions.All(e => e.Code != p.Code)))
        {
            var entity = new Permission { Code = p.Code, Module = p.Module, Action = p.Action, Description = p.Description };
            _db.Add(entity);
            permissions.Add(entity);
        }
        await _db.SaveChangesAsync(ct);
        var permissionIds = permissions.ToDictionary(p => p.Code, p => p.Id);

        var roles = new Dictionary<string, Role>();
        foreach (var (code, name) in new[] { (RoleCodes.SuperAdmin, "Super Admin") }.Concat(RoleTemplates.Roles))
        {
            var role = new Role
            {
                SchoolId = school.Id, BranchId = branch.Id, Code = code, Name = name,
                Description = $"{name} role", IsSystem = true
            };
            _db.Add(role);
            roles[code] = role;
        }
        await _db.SaveChangesAsync(ct);

        foreach (var (code, role) in roles)
        foreach (var permission in RoleTemplates.PermissionsFor(code))
            if (permissionIds.TryGetValue(permission, out var id))
                _db.Add(new RolePermission { RoleId = role.Id, PermissionId = id });

        var email = adminEmail.Trim().ToLowerInvariant();
        _db.Add(new UserAccount
        {
            SchoolId = school.Id, BranchId = branch.Id, Email = email, NormalizedEmail = email,
            FullName = "Super Admin", PasswordHash = _hasher.Hash(adminPassword),
            RoleId = roles[RoleCodes.SuperAdmin].Id, IsActive = true, EmailConfirmed = true
        });
        await _db.SaveChangesAsync(ct);
    }
}
