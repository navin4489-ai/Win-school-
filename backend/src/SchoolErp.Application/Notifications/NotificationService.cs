using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Notifications;

public sealed class NotificationService
{
    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IDateTimeProvider _clock;

    public NotificationService(IAppDbContext db, IUnitOfWork uow, ICurrentUser user, IDateTimeProvider clock)
    {
        _db = db;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    public async Task<List<NotificationDto>> ListAsync(bool unreadOnly, CancellationToken ct = default)
    {
        var userId = _user.UserId;
        var guardianId = _user.GuardianId;

        var query = _db.Notifications.Where(n => n.SchoolId == SchoolId && !n.IsDeleted &&
            ((userId != null && n.UserAccountId == userId) ||
             (guardianId != null && n.GuardianId == guardianId)));

        if (unreadOnly) query = query.Where(n => n.ReadAtUtc == null);

        return await query.OrderByDescending(n => n.Id).Take(100)
            .Select(n => new NotificationDto
            {
                Id = n.Id, Title = n.Title, Message = n.Message, Category = n.Category,
                Channel = n.Channel, Status = n.Status, CreatedAtUtc = n.CreatedAtUtc, ReadAtUtc = n.ReadAtUtc
            }).ToListAsync(ct);
    }

    public async Task MarkReadAsync(int id, CancellationToken ct = default)
    {
        var userId = _user.UserId;
        var guardianId = _user.GuardianId;

        var notification = await _db.Notifications.FirstOrDefaultAsync(n =>
            n.Id == id && n.SchoolId == SchoolId && !n.IsDeleted &&
            ((userId != null && n.UserAccountId == userId) || (guardianId != null && n.GuardianId == guardianId)), ct)
            ?? throw DomainException.NotFound("Notification");

        notification.ReadAtUtc = _clock.UtcNow;
        notification.Status = NotificationStatus.Read;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<int> CountUnreadAsync(CancellationToken ct = default)
    {
        var userId = _user.UserId;
        var guardianId = _user.GuardianId;

        return await _db.Notifications.CountAsync(n =>
            n.SchoolId == SchoolId && !n.IsDeleted && n.ReadAtUtc == null &&
            ((userId != null && n.UserAccountId == userId) || (guardianId != null && n.GuardianId == guardianId)), ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var userId = _user.UserId;
        var guardianId = _user.GuardianId;

        var pending = await _db.Notifications
            .Where(n => n.SchoolId == SchoolId && !n.IsDeleted && n.ReadAtUtc == null &&
                        ((userId != null && n.UserAccountId == userId) || (guardianId != null && n.GuardianId == guardianId)))
            .ToListAsync(ct);

        foreach (var notification in pending)
        {
            notification.ReadAtUtc = _clock.UtcNow;
            notification.Status = NotificationStatus.Read;
        }

        if (pending.Count > 0) await _uow.SaveChangesAsync(ct);
    }

    public async Task<int> BroadcastAsync(BroadcastRequest request, CancellationToken ct = default)
    {
        var roles = request.Audience.ToLowerInvariant() switch
        {
            "parents" => new[] { RoleCodes.Parent },
            "teachers" => new[] { RoleCodes.Teacher, RoleCodes.ClassCoordinator },
            "students" => new[] { RoleCodes.Student },
            "staff" => new[] { RoleCodes.Teacher, RoleCodes.ClassCoordinator, RoleCodes.Accountant, RoleCodes.HrManager,
                RoleCodes.Librarian, RoleCodes.TransportManager, RoleCodes.Administrator },
            _ => Array.Empty<string>()
        };

        var usersQuery =
            from u in _db.UserAccounts
            join r in _db.Roles on u.RoleId equals r.Id
            where u.SchoolId == SchoolId && u.IsActive && !u.IsDeleted
            select new { u, r.Code };

        if (roles.Length > 0) usersQuery = usersQuery.Where(x => roles.Contains(x.Code));

        var users = await usersQuery.Take(10000).ToListAsync(ct);

        var announcement = new Announcement
        {
            SchoolId = SchoolId,
            BranchId = _user.BranchId,
            Title = request.Title.Trim(),
            Body = request.Message.Trim(),
            Audience = request.Audience,
            PublishedAtUtc = _clock.UtcNow,
            CreatedByEmployeeId = _user.EmployeeId
        };
        _db.Add(announcement);

        foreach (var item in users)
        {
            _db.Add(new Notification
            {
                SchoolId = SchoolId,
                UserAccountId = item.u.Id,
                GuardianId = item.u.GuardianId,
                StudentId = item.u.StudentId,
                Title = request.Title.Trim(),
                Message = request.Message.Trim(),
                Channel = request.Channel,
                Status = NotificationStatus.Pending,
                Category = "Announcement",
                Recipient = request.Channel == NotificationChannel.Email ? item.u.Email : item.u.Phone
            });
        }

        await _uow.SaveChangesAsync(ct);
        return users.Count;
    }
}
