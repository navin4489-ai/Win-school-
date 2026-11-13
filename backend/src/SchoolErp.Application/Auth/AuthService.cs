using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;

namespace SchoolErp.Application.Auth;

public sealed class AuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(7);

    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _passwords;
    private readonly ITokenService _tokens;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;

    public AuthService(IAppDbContext db, IPasswordHasher passwords, ITokenService tokens,
        ICurrentUser currentUser, IUnitOfWork uow, IDateTimeProvider clock)
    {
        _db = db;
        _passwords = passwords;
        _tokens = tokens;
        _currentUser = currentUser;
        _uow = uow;
        _clock = clock;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var normalized = request.Email.Trim().ToLowerInvariant();
        var query = _db.UserAccounts.IgnoreQueryFilters()
            .Where(u => !u.IsDeleted && u.NormalizedEmail == normalized);

        if (!string.IsNullOrWhiteSpace(request.SchoolCode))
        {
            var schoolId = await _db.Schools
                .Where(s => s.Code == request.SchoolCode && s.IsActive)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync(ct);

            if (schoolId is null) throw DomainException.Unauthorized();
            query = query.Where(u => u.SchoolId == schoolId);
        }

        var candidates = await query.OrderBy(u => u.Id).ToListAsync(ct);
        var now = _clock.UtcNow;

        // The same email may exist in several schools. The school is resolved from the
        // account whose password matches; if that is still ambiguous, never guess.
        if (candidates.Count > 1)
        {
            var matches = candidates.Where(u => _passwords.Verify(u.PasswordHash, request.Password)).ToList();
            if (matches.Count == 0)
            {
                await AuditLoginAsync(null, normalized, false, "Invalid password", ct);
                throw DomainException.Unauthorized();
            }
            if (matches.Count > 1)
            {
                await AuditLoginAsync(null, normalized, false, "Email and password match several schools", ct);
                throw new DomainException("school_ambiguous",
                    "This account exists in more than one school. Contact your administrator.", 400);
            }
            candidates = matches;
        }

        var user = candidates.FirstOrDefault();

        if (user is null)
        {
            await AuditLoginAsync(null, normalized, false, "Unknown email", ct);
            throw DomainException.Unauthorized();
        }

        if (!user.IsActive || user.IsDeleted)
        {
            await AuditLoginAsync(user, normalized, false, "Inactive account", ct);
            throw DomainException.Unauthorized("This account is disabled.");
        }

        var schoolActive = await _db.Schools.AnyAsync(s => s.Id == user.SchoolId && s.IsActive, ct);
        if (!schoolActive)
        {
            await AuditLoginAsync(user, normalized, false, "School deactivated", ct);
            throw DomainException.Unauthorized("This school's account is not active. Contact the administrator.");
        }

        if (user.LockoutEndUtc is not null && user.LockoutEndUtc > now)
        {
            await AuditLoginAsync(user, normalized, false, "Account locked", ct);
            throw new DomainException("account_locked",
                $"Account is locked until {user.LockoutEndUtc:u} due to failed sign-in attempts.", 423);
        }

        if (!_passwords.Verify(user.PasswordHash, request.Password))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEndUtc = now.Add(LockoutDuration);
                user.FailedLoginCount = 0;
            }

            await SaveLoginBookkeepingAsync(ct);
            await AuditLoginAsync(user, normalized, false, "Invalid password", ct);
            throw DomainException.Unauthorized();
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;

        var response = await IssueTokensAsync(user, ct);
        await SaveLoginBookkeepingAsync(ct);
        await AuditLoginAsync(user, normalized, true, null, ct);
        return response;
    }

    /// <summary>
    /// Sign-in only touches bookkeeping columns (last login, failed attempts), so when the
    /// same account signs in on two devices at once the latest write simply wins instead of
    /// failing one of the sign-ins with a concurrency error.
    /// </summary>
    private async Task SaveLoginBookkeepingAsync(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _uow.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < 8)
            {
                foreach (var entry in ex.Entries)
                {
                    var current = await entry.GetDatabaseValuesAsync(ct);
                    if (current is null) throw;
                    entry.OriginalValues.SetValues(current);
                }
            }
        }
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw DomainException.Unauthorized("Refresh token is required.");
        var hash = _tokens.HashRefreshToken(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null) throw DomainException.Unauthorized("Invalid refresh token.");

        if (!stored.IsActive)
        {
            // Token reuse: assume theft and revoke the whole family for this user.
            var active = await _db.RefreshTokens
                .Where(t => t.UserAccountId == stored.UserAccountId && t.RevokedAtUtc == null)
                .ToListAsync(ct);

            foreach (var token in active)
            {
                token.RevokedAtUtc = _clock.UtcNow;
                token.RevokedReason = "Refresh token reuse detected";
            }

            await _uow.SaveChangesAsync(ct);
            throw DomainException.Unauthorized("Refresh token is no longer valid. Please sign in again.");
        }

        var user = await _db.UserAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == stored.UserAccountId && !u.IsDeleted, ct);

        if (user is null || !user.IsActive) throw DomainException.Unauthorized("Account is not available.");

        var response = await IssueTokensAsync(user, ct);
        stored.RevokedAtUtc = _clock.UtcNow;
        stored.RevokedReason = "Rotated";
        stored.ReplacedByTokenHash = _tokens.HashRefreshToken(response.RefreshToken);
        await _uow.SaveChangesAsync(ct);
        return response;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = _tokens.HashRefreshToken(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || stored.RevokedAtUtc is not null) return;

        stored.RevokedAtUtc = _clock.UtcNow;
        stored.RevokedReason = "Signed out";
        await _uow.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId ?? throw DomainException.Unauthorized();
        var user = await _db.UserAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct)
            ?? throw DomainException.NotFound("User");

        if (!_passwords.Verify(user.PasswordHash, request.CurrentPassword))
            throw DomainException.Validation("Current password is incorrect.");

        if (_passwords.Verify(user.PasswordHash, request.NewPassword))
            throw DomainException.Validation("New password must differ from the current password.");

        user.PasswordHash = _passwords.Hash(request.NewPassword);
        user.TokenVersion++;
        user.PasswordResetTokenHash = null;
        user.PasswordResetExpiresAtUtc = null;

        var tokens = await _db.RefreshTokens
            .Where(t => t.UserAccountId == user.Id && t.RevokedAtUtc == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = _clock.UtcNow;
            token.RevokedReason = "Password changed";
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task<UserProfileDto> GetProfileAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId ?? throw DomainException.Unauthorized();
        var user = await _db.UserAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct)
            ?? throw DomainException.NotFound("User");

        return await BuildProfileAsync(user, ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(UserAccount user, CancellationToken ct)
    {
        // Login runs before there is a school on the request, so the role is read across
        // the filter but pinned to the user's own school.
        var role = await _db.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == user.RoleId && r.SchoolId == user.SchoolId && !r.IsDeleted, ct)
            ?? throw DomainException.Unauthorized("The account role is not configured.");

        var permissions = await EffectivePermissionsAsync(user, role, ct);

        var access = _tokens.CreateAccessToken(user, role.Code, permissions);
        var refresh = _tokens.CreateRefreshToken();

        _db.Add(new RefreshToken
        {
            UserAccountId = user.Id,
            TokenHash = _tokens.HashRefreshToken(refresh),
            ExpiresAtUtc = _clock.UtcNow.Add(RefreshLifetime),
            CreatedAtUtc = _clock.UtcNow,
            CreatedByIp = _currentUser.IpAddress
        });

        var profile = await BuildProfileAsync(user, permissions, role, ct);
        return new AuthResponse
        {
            AccessToken = access.Token,
            RefreshToken = refresh,
            ExpiresAtUtc = access.ExpiresAtUtc,
            User = profile
        };
    }

    private async Task<UserProfileDto> BuildProfileAsync(UserAccount user, CancellationToken ct)
    {
        var role = await _db.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == user.RoleId, ct);
        var permissions = role is null ? new List<string>() : await EffectivePermissionsAsync(user, role, ct);

        return await BuildProfileAsync(user, permissions, role, ct);
    }

    /// <summary>
    /// The role's permissions minus those of modules the school has switched off. The
    /// super admin is never restricted, because they configure the modules.
    /// </summary>
    private async Task<List<string>> EffectivePermissionsAsync(UserAccount user, Role role, CancellationToken ct)
    {
        var codes = await (
            from rp in _db.RolePermissions
            join p in _db.Permissions on rp.PermissionId equals p.Id
            where rp.RoleId == role.Id
            select p.Code).Distinct().ToListAsync(ct);

        if (role.Code == RoleCodes.SuperAdmin) return codes;

        var modules = await _db.Schools.Where(s => s.Id == user.SchoolId)
            .Select(s => s.EnabledModules).FirstOrDefaultAsync(ct);
        var enabled = SchoolModules.Parse(modules);

        return codes.Where(code => SchoolModules.Allows(enabled, code)).ToList();
    }

    private async Task<UserProfileDto> BuildProfileAsync(UserAccount user, IEnumerable<string> permissions,
        Role? role, CancellationToken ct)
    {
        var school = await _db.Schools.FirstOrDefaultAsync(s => s.Id == user.SchoolId, ct);

        return new UserProfileDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = role?.Name ?? "Unknown",
            RoleCode = role?.Code ?? string.Empty,
            SchoolId = user.SchoolId,
            SchoolName = school?.Name ?? "School",
            SchoolLogoUrl = school?.LogoUrl,
            BranchId = user.BranchId,
            EmployeeId = user.EmployeeId,
            GuardianId = user.GuardianId,
            StudentId = user.StudentId,
            Permissions = permissions.Distinct().ToList()
        };
    }

    private async Task AuditLoginAsync(UserAccount? user, string email, bool success, string? reason, CancellationToken ct)
    {
        _db.Add(new AuditLog
        {
            SchoolId = user?.SchoolId ?? 0,
            UserAccountId = user?.Id,
            UserEmail = user?.Email ?? email,
            Action = success ? AuditActions.Login : AuditActions.LoginFailed,
            EntityName = nameof(UserAccount),
            EntityId = user?.Id.ToString(),
            AfterJson = success ? null : $"{{\"reason\":\"{reason}\"}}",
            IpAddress = _currentUser.IpAddress,
            UserAgent = _currentUser.UserAgent,
            CreatedAtUtc = _clock.UtcNow
        });

        await _uow.SaveChangesAsync(ct);
    }
}
