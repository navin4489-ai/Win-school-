using Microsoft.EntityFrameworkCore;
using SchoolErp.Application.Common;

namespace SchoolErp.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public UnitOfWork(AppDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

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
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
