using DistributedLockPoc.Api.Models;
using Medallion.Threading;
using MongoDB.Driver;

namespace DistributedLockPoc.Api.Services;

public class CounterService(IDistributedLockProvider providerLock, IMongoCollection<Counter> counters, ILogger<CounterService> logger) : ICounterService
{

    /// <summary>
    /// Increments a named counter using a distributed lock to prevent race conditions.
    /// Only one instance can increment the counter at a time across all API replicas.
    /// </summary>
    public async Task<Counter> IncrementWithLockAsync(string counterName, CancellationToken ct = default)
    {
        var lockName = $"counter:{counterName}";

        logger.LogInformation("Attempting to acquire lock '{Lock}' for counter '{Counter}'", lockName, counterName);

        var @lock = providerLock.CreateLock(lockName);

        await using var handle = await @lock.AcquireAsync(cancellationToken: ct);

        logger.LogInformation("Lock '{Lock}' acquired. Incrementing counter...", lockName);

        var counter = await GetOrCreateAsync(counterName, ct);
        counter.Value++;
        counter.LastUpdatedAt = DateTime.UtcNow;
        counter.UpdatedBy = Environment.MachineName;

        await counters.ReplaceOneAsync(
            Builders<Counter>.Filter.Eq(c => c.Id, counter.Id),
            counter,
            new ReplaceOptions { IsUpsert = true },
            ct);

        logger.LogInformation("Counter '{Counter}' incremented to {Value}. Releasing lock.", counterName, counter.Value);

        return counter;
    }

    /// <summary>
    /// Increments WITHOUT a lock — used to demonstrate race conditions for comparison.
    /// </summary>
    public async Task<Counter> IncrementWithoutLockAsync(string counterName, CancellationToken ct = default)
    {
        var counter = await GetOrCreateAsync(counterName, ct);

        // Simulate a small delay to make races more likely (read-modify-write gap)
        await Task.Delay(Random.Shared.Next(1, 5), ct);

        counter.Value++;
        counter.LastUpdatedAt = DateTime.UtcNow;
        counter.UpdatedBy = Environment.MachineName;

        await counters.ReplaceOneAsync(
            Builders<Counter>.Filter.Eq(c => c.Id, counter.Id),
            counter,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return counter;
    }

    public async Task<Counter?> GetAsync(string counterName, CancellationToken ct = default)
    {
        return await counters
            .Find(Builders<Counter>.Filter.Eq(c => c.Name, counterName))
            .FirstOrDefaultAsync(ct);
    }

    public async Task ResetAsync(string counterName, CancellationToken ct = default)
    {
        await counters.DeleteOneAsync(
            Builders<Counter>.Filter.Eq(c => c.Name, counterName), ct);
    }

    private async Task<Counter> GetOrCreateAsync(string name, CancellationToken ct)
    {
        var existing = await counters
            .Find(Builders<Counter>.Filter.Eq(c => c.Name, name))
            .FirstOrDefaultAsync(ct);

        if (existing is not null) return existing;

        var newCounter = new Counter
        {
            Name = name,
            Value = 0,
            LastUpdatedAt = DateTime.UtcNow,
            UpdatedBy = Environment.MachineName
        };

        await counters.InsertOneAsync(newCounter, cancellationToken: ct);
        return newCounter;
    }
}
