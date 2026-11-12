using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
using SchoolErp.Infrastructure.Persistence;

namespace SchoolErp.Infrastructure.Services;

/// <summary>
/// Drains pending notifications. Runs outside a request, so tenant filters are
/// deliberately bypassed and the school id travels on each notification row.
/// </summary>
public sealed class NotificationDispatcher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private const int BatchSize = 50;
    private const int MaxRetries = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(IServiceScopeFactory scopeFactory, ILogger<NotificationDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification dispatch batch failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var pending = await db.Notifications.IgnoreQueryFilters()
            .Where(n => !n.IsDeleted && n.Status == NotificationStatus.Pending && n.RetryCount < MaxRetries)
            .OrderBy(n => n.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        foreach (var notification in pending)
        {
            var recipient = notification.Recipient;
            try
            {
                if (string.IsNullOrWhiteSpace(recipient))
                    recipient = await ResolveRecipientAsync(db, notification, ct);

                await sender.SendAsync(notification, recipient, ct);

                notification.Status = NotificationStatus.Sent;
                notification.SentAtUtc = clock.UtcNow;
                notification.Error = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notification.RetryCount++;
                notification.Error = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
                notification.Status = notification.RetryCount >= MaxRetries
                    ? NotificationStatus.Failed
                    : NotificationStatus.Pending;

                _logger.LogWarning(ex, "Notification {Id} failed (attempt {Attempt})", notification.Id,
                    notification.RetryCount);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<string?> ResolveRecipientAsync(AppDbContext db, Notification notification,
        CancellationToken ct)
    {
        if (notification.UserAccountId is not null)
        {
            var user = await db.UserAccounts.IgnoreQueryFilters()
                .Where(u => u.Id == notification.UserAccountId)
                .Select(u => new { u.Email, u.Phone })
                .FirstOrDefaultAsync(ct);

            if (notification.Channel == NotificationChannel.Email) return user?.Email;
            if (user?.Phone is not null) return user.Phone;
            return user?.Email;
        }

        if (notification.GuardianId is not null)
        {
            var guardian = await db.Guardians.IgnoreQueryFilters()
                .Where(g => g.Id == notification.GuardianId)
                .Select(g => new { g.Email, g.Phone })
                .FirstOrDefaultAsync(ct);

            if (notification.Channel == NotificationChannel.Email) return guardian?.Email;
            return guardian?.Phone ?? guardian?.Email;
        }

        if (notification.StudentId is not null)
        {
            return await db.Students.IgnoreQueryFilters()
                .Where(s => s.Id == notification.StudentId)
                .Select(s => s.Phone ?? s.Email)
                .FirstOrDefaultAsync(ct);
        }

        return null;
    }
}
