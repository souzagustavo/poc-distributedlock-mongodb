using DistributedLockPoc.Api.Models;
using Medallion.Threading.MongoDB;
using MongoDB.Driver;

namespace DistributedLockPoc.Api.Services
{
    public interface ICounterService
    {
        Task<Counter> IncrementWithLockAsync(string counterName, CancellationToken ct = default);
        Task<Counter> IncrementWithoutLockAsync(string counterName, CancellationToken ct = default);
        Task<Counter?> GetAsync(string counterName, CancellationToken ct = default);
        Task ResetAsync(string counterName, CancellationToken ct = default);
    }
}
