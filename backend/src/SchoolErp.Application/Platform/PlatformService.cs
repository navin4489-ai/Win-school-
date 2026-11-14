using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;

namespace SchoolErp.Application.Platform;

/// <summary>
/// Platform operations for the super admin: onboarding schools, branding, switching
/// modules and keeping each school's user list.
///
/// These are the only queries in the system that deliberately read across schools, so
/// every one of them pins the school explicitly and is gated on <see cref="ICurrentUser.IsPlatformAdmin"/>.
/// </summary>
public sealed class PlatformService
{
    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IPasswordHasher _passwords;
    private readonly IDateTimeProvider _clock;

    public PlatformService(IAppDbContext db, IUnitOfWork uow, ICurrentUser user, IPasswordHasher passwords,
        IDateTimeProvider clock)
    {
        _db = db;
        _uow = uow;
        _user = user;
        _passwords = passwords;
        _clock = clock;
    }

    private void EnsurePlatformAdmin()
    {
        if (!_user.IsPlatformAdmin) throw DomainException.Forbidden("Only the super admin can manage schools.");
    }

    public IReadOnlyList<ModuleDto> GetModules() =>
        SchoolModules.Toggleable.Select(m => new ModuleDto { Key = m.Key, Label = m.Label }).ToList();

    // ---------------------------------------------------------------- schools

    public async Task<List<SchoolSummaryDto>> GetSchoolsAsync(CancellationToken ct = default)
    {
        EnsurePlatformAdmin();

        var schools = await _db.Schools.OrderBy(s => s.Name).ToListAsync(ct);

        var users = await _db.UserAccounts.IgnoreQueryFilters().Where(u => !u.IsDeleted)
            .GroupBy(u => u.SchoolId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var students = await _db.Students.IgnoreQueryFilters().Where(s => !s.IsDeleted)
            .GroupBy(s => s.SchoolId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return schools.Select(s => Map(s, users.GetValueOrDefault(s.Id), students.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<SchoolSummaryDto> GetSchoolAsync(int id, CancellationToken ct = default)
    {
        EnsurePlatformAdmin();
        var school = await _db.Schools.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw DomainException.NotFound("School");
        return Map(school, 0, 0);
    }

    /// <summary>
    /// Creates a school with its main branch, current session, the standard roles and a
    /// School Owner account, so the school can sign in immediately.
    /// </summary>
    public async Task<SchoolSummaryDto> CreateSchoolAsync(CreateSchoolRequest request, CancellationToken ct = default)
    {
        EnsurePlatformAdmin();
        ValidatePassword(request.AdminPassword);

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Schools.AnyAsync(s => s.Code == code, ct))
            throw DomainException.Conflict($"The school code {code} is already in use.");

        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var school = new School { Code = code, CreatedAtUtc = _clock.UtcNow };
            Apply(school, request);
            _db.Add(school);
            await _uow.SaveChangesAsync(token);

            var branch = new Branch
            {
                SchoolId = school.Id, Name = "Main campus", Code = "MAIN", IsMainBranch = true,
                Email = school.Email, Phone = school.Phone, Address = school.AddressLine1
            };
            _db.Add(branch);
            await _uow.SaveChangesAsync(token);

            // Indian academic year: April to March.
            var today = _clock.Today;
            var startYear = today.Month >= 4 ? today.Year : today.Year - 1;
            _db.Add(new AcademicSession
            {
                SchoolId = school.Id, BranchId = branch.Id,
                Name = $"{startYear}-{(startYear + 1) % 100:00}",
                StartDate = new DateOnly(startYear, 4, 1), EndDate = new DateOnly(startYear + 1, 3, 31),
                IsCurrent = true
            });

            var permissionIds = await EnsurePermissionsAsync(token);
            var roles = new Dictionary<string, Role>();
            foreach (var (roleCode, name) in RoleTemplates.Roles)
            {
                var role = new Role
                {
                    SchoolId = school.Id, BranchId = branch.Id, Code = roleCode, Name = name,
                    Description = $"{name} role", IsSystem = true
                };
                _db.Add(role);
                roles[roleCode] = role;
            }

            await _uow.SaveChangesAsync(token);

            foreach (var (roleCode, role) in roles)
            {
                foreach (var permission in RoleTemplates.PermissionsFor(roleCode))
                {
                    if (permissionIds.TryGetValue(permission, out var permissionId))
                        _db.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionId });
                }
            }

            var email = request.AdminEmail.Trim().ToLowerInvariant();
            var admin = new UserAccount
            {
                SchoolId = school.Id, BranchId = branch.Id, Email = email, NormalizedEmail = email,
                FullName = request.AdminFullName.Trim(), PasswordHash = _passwords.Hash(request.AdminPassword),
                RoleId = roles[RoleCodes.SchoolOwner].Id, IsActive = true, EmailConfirmed = true
            };
            _db.Add(admin);

            await _uow.SaveChangesAsync(token);
            await EnsureEmployeeAsync(admin, roles[RoleCodes.SchoolOwner], token);
            return Map(school, 1, 0);
        }, ct);
    }

    public async Task<SchoolSummaryDto> UpdateSchoolAsync(int id, SaveSchoolRequest request, CancellationToken ct = default)
    {
        EnsurePlatformAdmin();
        var school = await _db.Schools.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw DomainException.NotFound("School");

        Apply(school, request);
        school.ModifiedAtUtc = _clock.UtcNow;
        await _uow.SaveChangesAsync(ct);
        return Map(school, 0, 0);
    }

    public async Task SetSchoolActiveAsync(int id, bool isActive, CancellationToken ct = default)
    {
        EnsurePlatformAdmin();
        var school = await _db.Schools.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw DomainException.NotFound("School");

        if (!isActive && school.Id == _user.SchoolId)
            throw DomainException.Validation("You cannot deactivate the school your own account belongs to.");

        school.IsActive = isActive;
        school.ModifiedAtUtc = _clock.UtcNow;

        // Signing everyone out makes a suspension take effect now, not when tokens expire.
        if (!isActive)
        {
            var userIds = await _db.UserAccounts.IgnoreQueryFilters()
                .Where(u => u.SchoolId == id).Select(u => u.Id).ToListAsync(ct);
            var tokens = await _db.RefreshTokens
                .Where(t => userIds.Contains(t.UserAccountId) && t.RevokedAtUtc == null).ToListAsync(ct);
            foreach (var token in tokens)
            {
                token.RevokedAtUtc = _clock.UtcNow;
                token.RevokedReason = "School deactivated";
            }
        }

        await _uow.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ users

    /// <summary>
    /// The school whose users are being managed. The super admin may name any school;
    /// everyone else is always held to their own, whatever they send.
    /// </summary>
    private int ResolveSchool(int? requested)
    {
        var own = _user.SchoolId ?? throw DomainException.Unauthorized();
        if (requested is null || requested == own) return own;
        if (!_user.IsPlatformAdmin) throw DomainException.Forbidden("You can only manage users of your own school.");
        return requested.Value;
    }

    public async Task<List<RoleOptionDto>> GetRolesAsync(int? schoolId, CancellationToken ct = default)
    {
        var target = ResolveSchool(schoolId);

        var roles = await _db.Roles.IgnoreQueryFilters()
            .Where(r => r.SchoolId == target && !r.IsDeleted)
            .OrderBy(r => r.Name).ToListAsync(ct);

        var roleIds = roles.Select(r => r.Id).ToList();
        var counts = await _db.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .GroupBy(rp => rp.RoleId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return roles
            .Where(r => _user.IsPlatformAdmin || r.Code != RoleCodes.SuperAdmin)
            .Select(r => new RoleOptionDto
            {
                Id = r.Id, Code = r.Code, Name = r.Name, PermissionCount = counts.GetValueOrDefault(r.Id)
            }).ToList();
    }

    public async Task<List<UserDto>> GetUsersAsync(int? schoolId, string? search, CancellationToken ct = default)
    {
        var target = ResolveSchool(schoolId);

        var query =
            from u in _db.UserAccounts.IgnoreQueryFilters()
            join r in _db.Roles.IgnoreQueryFilters() on u.RoleId equals r.Id
            where u.SchoolId == target && r.SchoolId == target && !u.IsDeleted
            select new { u, r };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.u.FullName.ToLower().Contains(term) || x.u.NormalizedEmail.Contains(term));
        }

        var now = _clock.UtcNow;
        var rows = await query.OrderBy(x => x.u.FullName).Take(500).ToListAsync(ct);

        return rows.Select(x => new UserDto
        {
            Id = x.u.Id, SchoolId = x.u.SchoolId, FullName = x.u.FullName, Email = x.u.Email, Phone = x.u.Phone,
            RoleId = x.r.Id, RoleCode = x.r.Code, RoleName = x.r.Name, IsActive = x.u.IsActive,
            LastLoginAtUtc = x.u.LastLoginAtUtc, IsLockedOut = x.u.LockoutEndUtc > now
        }).ToList();
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var target = ResolveSchool(request.SchoolId);
        ValidatePassword(request.Password);

        var role = await LoadAssignableRoleAsync(target, request.RoleId, ct);

        var email = request.Email.Trim().ToLowerInvariant();
        var duplicate = await _db.UserAccounts.IgnoreQueryFilters()
            .AnyAsync(u => u.SchoolId == target && u.NormalizedEmail == email && !u.IsDeleted, ct);
        if (duplicate) throw DomainException.Conflict("A user with this email already exists in the school.");

        var branchId = await _db.Branches.IgnoreQueryFilters()
            .Where(b => b.SchoolId == target && b.IsMainBranch && !b.IsDeleted)
            .Select(b => (int?)b.Id).FirstOrDefaultAsync(ct);

        var user = new UserAccount
        {
            SchoolId = target, BranchId = branchId, Email = email, NormalizedEmail = email,
            FullName = request.FullName.Trim(), Phone = request.Phone?.Trim(),
            PasswordHash = _passwords.Hash(request.Password), RoleId = role.Id, IsActive = true, EmailConfirmed = true
        };
        _db.Add(user);
        await _uow.SaveChangesAsync(ct);

        await EnsureEmployeeAsync(user, role, ct);
        return Map(user, role);
    }

    // Roles that are not staff never get an employee record.
    private static readonly HashSet<string> NonStaffRoles =
        new(StringComparer.Ordinal) { RoleCodes.Parent, RoleCodes.Student, RoleCodes.SuperAdmin };

    /// <summary>
    /// Staff accounts need an employee record: teacher lists, class assignments, check-in and
    /// payroll all work on employees, not on sign-ins. Creates and links one if missing.
    /// </summary>
    private async Task EnsureEmployeeAsync(UserAccount user, Role role, CancellationToken ct)
    {
        if (user.EmployeeId is not null || NonStaffRoles.Contains(role.Code)) return;

        var existing = await _db.Employees.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.SchoolId == user.SchoolId && !e.IsDeleted && e.Email == user.Email, ct);

        if (existing is null)
        {
            var parts = user.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            existing = new Employee
            {
                SchoolId = user.SchoolId, BranchId = user.BranchId,
                EmployeeNo = await NextEmployeeNoAsync(user.SchoolId, ct),
                FirstName = parts.ElementAtOrDefault(0) ?? user.Email,
                LastName = parts.ElementAtOrDefault(1) ?? string.Empty,
                Email = user.Email, Phone = user.Phone, JoiningDate = _clock.Today
            };
            _db.Add(existing);
            await _uow.SaveChangesAsync(ct);
        }

        user.EmployeeId = existing.Id;
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<string> NextEmployeeNoAsync(int schoolId, CancellationToken ct)
    {
        var year = _clock.Today.Year;
        var count = await _db.Employees.IgnoreQueryFilters().CountAsync(e => e.SchoolId == schoolId, ct);
        for (var i = count + 1; i < count + 10000; i++)
        {
            var candidate = $"EMP-{year}-{i:0000}";
            var taken = await _db.Employees.IgnoreQueryFilters()
                .AnyAsync(e => e.SchoolId == schoolId && e.EmployeeNo == candidate, ct);
            if (!taken) return candidate;
        }

        throw DomainException.Conflict("Employee number sequence exhausted.");
    }

    public async Task<UserDto> UpdateUserAsync(int id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(id, ct);
        var role = await LoadAssignableRoleAsync(user.SchoolId, request.RoleId, ct);

        if (user.Id == _user.UserId && (!request.IsActive || role.Id != user.RoleId))
            throw DomainException.Validation("You cannot deactivate yourself or change your own role.");

        var roleChanged = user.RoleId != role.Id;
        user.FullName = request.FullName.Trim();
        user.Phone = request.Phone?.Trim();
        user.RoleId = role.Id;
        user.IsActive = request.IsActive;

        // A role change or deactivation must not wait for the old token to expire.
        if (roleChanged || !request.IsActive) await RevokeSessionsAsync(user, "Account changed by administrator", ct);

        await _uow.SaveChangesAsync(ct);
        await EnsureEmployeeAsync(user, role, ct);
        return Map(user, role);
    }

    public async Task ResetPasswordAsync(int id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        ValidatePassword(request.NewPassword);
        var user = await LoadUserAsync(id, ct);

        user.PasswordHash = _passwords.Hash(request.NewPassword);
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await RevokeSessionsAsync(user, "Password reset by administrator", ct);

        await _uow.SaveChangesAsync(ct);
    }

    // -------------------------------------------------------------- internals

    private async Task<UserAccount> LoadUserAsync(int id, CancellationToken ct)
    {
        var user = await _db.UserAccounts.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);

        // Another school's user is reported as missing, never as forbidden, so ids
        // cannot be probed across schools.
        if (user is null || (user.SchoolId != _user.SchoolId && !_user.IsPlatformAdmin))
            throw DomainException.NotFound("User");

        // Nobody may manage an account ranked at or above their own (e.g. a school
        // administrator resetting the owner's or the super admin's password).
        if (user.Id != _user.UserId)
        {
            var targetRole = await _db.Roles.IgnoreQueryFilters()
                .Where(r => r.Id == user.RoleId).Select(r => r.Code).FirstOrDefaultAsync(ct) ?? string.Empty;
            if (RoleRank(targetRole) >= CallerRank())
                throw DomainException.Forbidden("You cannot manage an account with this role.");
        }

        return user;
    }

    private static int RoleRank(string roleCode) => roleCode switch
    {
        RoleCodes.SuperAdmin => 3,
        RoleCodes.SchoolOwner => 2,
        _ => 1
    };

    private int CallerRank() =>
        _user.IsPlatformAdmin ? 4 : _user.IsInRole(RoleCodes.SchoolOwner) ? 3 : 2;

    private async Task<Role> LoadAssignableRoleAsync(int schoolId, int roleId, CancellationToken ct)
    {
        var role = await _db.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == roleId && r.SchoolId == schoolId && !r.IsDeleted, ct)
            ?? throw DomainException.Validation("The role does not belong to this school.");

        if (role.Code == RoleCodes.SuperAdmin && !_user.IsPlatformAdmin)
            throw DomainException.Forbidden("Only the super admin can grant the Super Admin role.");

        if (role.Code == RoleCodes.SchoolOwner && !_user.IsPlatformAdmin && !_user.IsInRole(RoleCodes.SchoolOwner))
            throw DomainException.Forbidden("Only the school owner or the super admin can grant the School Owner role.");

        return role;
    }

    private async Task RevokeSessionsAsync(UserAccount user, string reason, CancellationToken ct)
    {
        user.TokenVersion++;
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserAccountId == user.Id && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var token in tokens)
        {
            token.RevokedAtUtc = _clock.UtcNow;
            token.RevokedReason = reason;
        }
    }

    private async Task<Dictionary<string, int>> EnsurePermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.ToListAsync(ct);
        foreach (var permission in Permissions.All.Where(p => existing.All(e => e.Code != p.Code)))
        {
            var entity = new Permission
            {
                Code = permission.Code, Module = permission.Module, Action = permission.Action,
                Description = permission.Description
            };
            _db.Add(entity);
            existing.Add(entity);
        }

        await _uow.SaveChangesAsync(ct);
        return existing.ToDictionary(p => p.Code, p => p.Id);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 ||
            !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw DomainException.Validation("The password must be at least 8 characters and contain letters and digits.");
    }

    private static void Apply(School school, SaveSchoolRequest request)
    {
        school.Name = request.Name.Trim();
        school.LegalName = request.LegalName?.Trim();
        school.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
        school.Email = request.Email?.Trim();
        school.Phone = request.Phone?.Trim();
        school.AddressLine1 = request.AddressLine1?.Trim();
        school.City = request.City?.Trim();
        school.State = request.State?.Trim();
        school.PostalCode = request.PostalCode?.Trim();
        school.EnabledModules = SchoolModules.Serialize(request.EnabledModules);
        school.PlatformFeeType = string.IsNullOrWhiteSpace(request.PlatformFeeType) ? null : request.PlatformFeeType;
        school.PlatformFeeAmount = school.PlatformFeeType is null
            ? 0 : Math.Round(request.PlatformFeeAmount, 2, MidpointRounding.AwayFromZero);
        school.PlatformFeeNotes = string.IsNullOrWhiteSpace(request.PlatformFeeNotes) ? null : request.PlatformFeeNotes.Trim();
    }

    private static SchoolSummaryDto Map(School s, int users, int students) => new()
    {
        Id = s.Id, Code = s.Code, Name = s.Name, LegalName = s.LegalName, LogoUrl = s.LogoUrl, Email = s.Email,
        Phone = s.Phone, AddressLine1 = s.AddressLine1, City = s.City, State = s.State, PostalCode = s.PostalCode,
        IsActive = s.IsActive, EnabledModules = SchoolModules.Parse(s.EnabledModules).OrderBy(m => m).ToList(),
        PlatformFeeType = s.PlatformFeeType, PlatformFeeAmount = s.PlatformFeeAmount, PlatformFeeNotes = s.PlatformFeeNotes,
        UserCount = users, StudentCount = students, CreatedAtUtc = s.CreatedAtUtc
    };

    private static UserDto Map(UserAccount u, Role r) => new()
    {
        Id = u.Id, SchoolId = u.SchoolId, FullName = u.FullName, Email = u.Email, Phone = u.Phone, RoleId = r.Id,
        RoleCode = r.Code, RoleName = r.Name, IsActive = u.IsActive, LastLoginAtUtc = u.LastLoginAtUtc
    };
}
