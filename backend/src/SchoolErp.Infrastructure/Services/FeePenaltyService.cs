using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
using SchoolErp.Infrastructure.Persistence;

namespace SchoolErp.Infrastructure.Services;

public sealed class FeePenaltyOptions
{
    public const string SectionName = "Fees:Penalty";

    /// <summary>Late fee charged for every day an installment stays unpaid.</summary>
    public decimal DailyAmount { get; set; } = 10m;

    /// <summary>Day of the month by which the fee must be paid.</summary>
    public int PayByDay { get; set; } = 15;

    /// <summary>
    /// Cap on the total late fee of one installment, as a percentage of the fee
    /// itself. Without a cap an installment that has been unpaid for a whole year
    /// would collect a late fee larger than the school ever expects to recover.
    /// </summary>
    public decimal MaxPercentageOfFee { get; set; } = 25m;

    /// <summary>How often the job looks for newly overdue installments.</summary>
    public int IntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Accrues a daily late fee on unpaid installments and tells the guardian.
///
/// Policy: an installment must be paid by the <see cref="FeePenaltyOptions.PayByDay"/>th.
/// If the due date falls on or before that day the grace period ends in the same month,
/// otherwise it ends in the following month. The late fee then accrues from the next day
/// at <see cref="FeePenaltyOptions.DailyAmount"/> per day until the installment is settled.
///
/// The job is idempotent: a row is written per installment per day and the unique index
/// on (FeeInstallmentId, PenaltyDate) means a re-run cannot double charge.
/// </summary>
public sealed class FeePenaltyService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FeePenaltyService> _logger;
    private readonly FeePenaltyOptions _options;

    public FeePenaltyService(IServiceScopeFactory scopeFactory, ILogger<FeePenaltyService> logger,
        IOptions<FeePenaltyOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(5, _options.IntervalMinutes));

        // Run once at start-up so a freshly seeded database is already up to date.
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fee penalty run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        if (_options.DailyAmount <= 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var today = clock.Today;

        var overdue =
            from i in db.FeeInstallments.IgnoreQueryFilters()
            join a in db.StudentFeeAssignments.IgnoreQueryFilters() on i.StudentFeeAssignmentId equals a.Id
            where i.SchoolId == a.SchoolId && !i.IsDeleted && !a.IsDeleted && a.IsActive &&
                  i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived &&
                  i.Amount + i.PenaltyAmount > i.PaidAmount
            select new { Installment = i, a.StudentId, a.SchoolId };

        var candidates = await overdue.ToListAsync(ct);
        if (candidates.Count == 0) return;

        var installmentIds = candidates.Select(x => x.Installment.Id).ToList();
        var existing = await db.FeePenalties.IgnoreQueryFilters()
            .Where(p => installmentIds.Contains(p.FeeInstallmentId))
            .Select(p => new { p.FeeInstallmentId, p.PenaltyDate })
            .ToListAsync(ct);

        var alreadyCharged = existing
            .GroupBy(p => p.FeeInstallmentId)
            .ToDictionary(g => g.Key, g => new HashSet<DateOnly>(g.Select(p => p.PenaltyDate)));

        var chargedToday = new List<(int StudentId, int SchoolId, FeePenalty Penalty)>();

        foreach (var candidate in candidates)
        {
            var installment = candidate.Installment;
            var penaltyStart = CalculatePenaltyStart(installment.DueDate);
            if (today < penaltyStart) continue;

            var daysToCharge = today.DayNumber - penaltyStart.DayNumber + 1;
            if (daysToCharge <= 0) continue;

            // Never let the late fee run past the configured share of the fee itself.
            var cap = Math.Round(installment.Amount * _options.MaxPercentageOfFee / 100m, 2);
            var headroom = Math.Max(0m, cap - installment.PenaltyAmount);
            var allowedDays = (int)Math.Floor(headroom / _options.DailyAmount);
            daysToCharge = Math.Min(daysToCharge, allowedDays);
            if (daysToCharge <= 0) continue;

            alreadyCharged.TryGetValue(installment.Id, out var chargedDates);
            chargedDates ??= new HashSet<DateOnly>();

            for (var offset = 0; offset < daysToCharge; offset++)
            {
                var date = penaltyStart.AddDays(offset);
                if (!chargedDates.Add(date)) continue;

                var penalty = new FeePenalty
                {
                    SchoolId = candidate.SchoolId,
                    StudentFeeAssignmentId = installment.StudentFeeAssignmentId,
                    FeeInstallmentId = installment.Id,
                    StudentId = candidate.StudentId,
                    PenaltyDate = date,
                    Amount = _options.DailyAmount,
                    DaysLate = offset + 1,
                    Reason = $"Unpaid on {installment.DueDate:dd MMM yyyy}; daily late fee after the {_options.PayByDay}th."
                };

                db.FeePenalties.Add(penalty);
                installment.PenaltyAmount = Math.Round(installment.PenaltyAmount + _options.DailyAmount, 2);
                chargedToday.Add((candidate.StudentId, candidate.SchoolId, penalty));
            }
        }

        if (chargedToday.Count == 0) return;

        await db.SaveChangesAsync(ct);
        await QueueNotificationsAsync(db, chargedToday, ct);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Applied {Count} late fee day(s) totalling {Amount}.",
            chargedToday.Count, chargedToday.Sum(x => x.Penalty.Amount));
    }

    /// <summary>
    /// The first day a late fee can be charged. The fee is payable by the configured day
    /// of the month; a fee due after that day rolls into the following month.
    /// </summary>
    internal DateOnly CalculatePenaltyStart(DateOnly dueDate)
    {
        var payByDay = Math.Clamp(_options.PayByDay, 1, 28);
        var payByMonth = dueDate.Day <= payByDay ? dueDate.Month : dueDate.Month + 1;
        var payByDate = payByMonth > 12
            ? new DateOnly(dueDate.Year + 1, 1, payByDay)
            : new DateOnly(dueDate.Year, payByMonth, payByDay);

        return payByDate.AddDays(1);
    }

    private async Task QueueNotificationsAsync(AppDbContext db,
        List<(int StudentId, int SchoolId, FeePenalty Penalty)> charged, CancellationToken ct)
    {
        var studentIds = charged.Select(x => x.StudentId).Distinct().ToList();

        var guardians = await (
            from sp in db.StudentParents.IgnoreQueryFilters()
            join g in db.Guardians.IgnoreQueryFilters() on sp.GuardianId equals g.Id
            where studentIds.Contains(sp.StudentId) && sp.IsPrimary && !sp.IsDeleted && !g.IsDeleted
            select new { sp.StudentId, GuardianId = g.Id, g.UserAccountId, g.Phone }).ToListAsync(ct);

        var guardianByStudent = guardians
            .GroupBy(g => g.StudentId)
            .ToDictionary(g => g.Key, g => g.First());

        var studentNames = await db.Students.IgnoreQueryFilters()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName })
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        foreach (var group in charged.GroupBy(x => x.StudentId))
        {
            if (!guardianByStudent.TryGetValue(group.Key, out var guardian)) continue;

            var total = group.Sum(x => x.Penalty.Amount);
            var school = group.First().SchoolId;
            var studentName = studentNames.GetValueOrDefault(group.Key, "your child");
            var days = group.Select(x => x.Penalty.PenaltyDate).Distinct().Count();

            db.Notifications.Add(new Notification
            {
                SchoolId = school,
                StudentId = group.Key,
                GuardianId = guardian.GuardianId,
                UserAccountId = guardian.UserAccountId,
                Title = "Late fee added to unpaid fees",
                Message = $"A late fee of {total:0.00} has been added because the fees of {studentName} " +
                          $"were not paid by the {_options.PayByDay}th. Please clear the outstanding amount.",
                Channel = NotificationChannel.InApp,
                Status = NotificationStatus.Pending,
                Category = "FeePenalty",
                Recipient = guardian.Phone
            });
        }
    }
}
