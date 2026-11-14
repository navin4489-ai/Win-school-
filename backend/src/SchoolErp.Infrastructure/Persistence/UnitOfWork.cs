using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;

namespace SchoolErp.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public UnitOfWork(AppDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task LockAsync(string key, CancellationToken ct = default)
    {
        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction is null) return;

        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.Ordinal))
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({key}))", ct);
        else if (provider.Contains("SqlServer", StringComparison.Ordinal))
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sp_getapplock @Resource = {key}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000", ct);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        // InMemory provider (unit tests) does not support transactions.
        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction is not null)
            return await action(ct);

        // A retrying execution strategy only allows user transactions inside its own
        // ExecuteAsync, so the whole unit is retried together after a transient failure.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(token);
            try
            {
                var result = await action(token);
                await transaction.CommitAsync(token);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(token);
                throw;
            }
        }, ct);
    }
}

public sealed class SystemClock : IDateTimeProvider
{
    // Schools run on Indian Standard Time; "today" must flip at local midnight, not UTC.
    private static readonly TimeZoneInfo SchoolZone = ResolveZone();

    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SchoolZone));

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { Environment.GetEnvironmentVariable("SCHOOL_TIME_ZONE") ?? "Asia/Kolkata", "India Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");
    }
}
