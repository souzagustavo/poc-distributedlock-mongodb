using DistributedLockPoc.Api.Services;

namespace DistributedLockPoc.Api.Endpoints;

public static class CounterEndpoints
{
    public static IEndpointRouteBuilder MapCounterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/counters")
            .WithTags("Counters");

        // ---- WITH LOCK ----
        group.MapPost("/{name}/increment", async (string name, ICounterService svc, CancellationToken ct) =>
        {
            var counter = await svc.IncrementWithLockAsync(name, ct);
            return Results.Ok(counter);
        })
        .WithSummary("Increment counter (with distributed lock)")
        .WithDescription("Uses MongoDistributedLock to safely increment the counter under high concurrency.");

        // ---- WITHOUT LOCK (race condition demo) ----
        group.MapPost("/{name}/increment-unsafe", async (string name, ICounterService svc, CancellationToken ct) =>
        {
            var counter = await svc.IncrementWithoutLockAsync(name, ct);
            return Results.Ok(counter);
        })
        .WithSummary("Increment counter (NO lock — race condition demo)")
        .WithDescription("Increments without a lock. Under load, the final value will be lower than expected due to lost updates.");

        // ---- GET ----
        group.MapGet("/{name}", async (string name, ICounterService svc, CancellationToken ct) =>
        {
            var counter = await svc.GetAsync(name, ct);
            return counter is null ? Results.NotFound() : Results.Ok(counter);
        })
        .WithSummary("Get current counter value");

        // ---- RESET ----
        group.MapDelete("/{name}", async (string name, ICounterService svc, CancellationToken ct) =>
        {
            await svc.ResetAsync(name, ct);
            return Results.NoContent();
        })
        .WithSummary("Reset (delete) a counter");

        return app;
    }
}
