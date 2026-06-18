using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DistributedLockPoc.Api.Models;
using FluentAssertions;
using Xunit;

namespace DistributedLockPoc.IntegrationTests;

[Collection("Aspire")]
public class DistributedLockTests(AspireFixture fixture)
{
    private readonly HttpClient _client = fixture.ApiClient!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ──────────────────────────────────────────────────────────
    //  Basic happy-path
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task IncrementWithLock_SingleRequest_ReturnsValueOne()
    {
        var name = $"test-single-{Guid.NewGuid():N}";

        var response = await _client.PostAsync($"/counters/{name}/increment", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var counter = await ParseCounterAsync(response);
        
        counter!.Value!.Should().Be(1);
        counter!.Name.Should().Be(name);
    }

    [Fact]
    public async Task IncrementWithLock_Sequential_ValueMatchesCount()
    {
        const int iterations = 10;
        var name = $"test-seq-{Guid.NewGuid():N}";

        for (var i = 0; i < iterations; i++)
            await _client.PostAsync($"/counters/{name}/increment", null);

        var counter = await GetCounterAsync(name);
        counter!.Value!.Should().Be(iterations,
            because: "sequential increments with a lock must never lose an update");
    }

    // ──────────────────────────────────────────────────────────
    //  Concurrency — the key scenario
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task IncrementWithLock_Concurrent_NoLostUpdates()
    {
        const int concurrency = 30;
        var name = $"test-concurrent-locked-{Guid.NewGuid():N}";

        // Fire all requests in parallel — only one lock holder at a time
        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => _client.PostAsync($"/counters/{name}/increment", null));

        var responses = await Task.WhenAll(tasks);
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));

        var counter = await GetCounterAsync(name);
        counter!.Value!.Should().Be(concurrency,
            because: "distributed lock must prevent lost updates under concurrent load");
    }

    [Fact]
    public async Task IncrementWithoutLock_Concurrent_LikelyLosesUpdates()
    {
        // This test documents the race-condition behaviour of the unsafe endpoint.
        // With concurrency high enough, the final value WILL be < expected on most runs.
        const int concurrency = 30;
        var name = $"test-concurrent-unsafe-{Guid.NewGuid():N}";

        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => _client.PostAsync($"/counters/{name}/increment-unsafe", null));

        await Task.WhenAll(tasks);

        var counter = await GetCounterAsync(name);
        var actual = counter!.Value!;

        // We can't assert the exact value, but we document that it's ≤ expected.
        // In practice it's almost always < concurrency.
        actual.Should().BeLessThanOrEqualTo(concurrency,
            because: "without a lock, concurrent read-modify-write causes lost updates");

        // Log for visibility in CI output
        Console.WriteLine($"[Race demo] Expected={concurrency}, Actual={actual}, Lost={concurrency - actual}");
    }

    // ──────────────────────────────────────────────────────────
    //  Lock correctness — verify mutual exclusion ordering
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task IncrementWithLock_HighConcurrency_AllResponsesSucceed()
    {
        const int concurrency = 50;
        var name = $"test-hi-{Guid.NewGuid():N}";

        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => _client.PostAsync($"/counters/{name}/increment", null));

        var responses = await Task.WhenAll(tasks);

        var failures = responses.Where(r => !r.IsSuccessStatusCode).ToList();
        failures.Should().BeEmpty("the lock should never cause a 5xx — it serialises, not rejects");

        var counter = await GetCounterAsync(name);
        counter!.Value!.Should().Be(concurrency);
    }

    // ──────────────────────────────────────────────────────────
    //  Get & Reset
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCounter_AfterIncrement_ReturnsCorrectValue()
    {
        var name = $"test-get-{Guid.NewGuid():N}";
        await _client.PostAsync($"/counters/{name}/increment", null);

        var getResp = await _client.GetAsync($"/counters/{name}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var counter = await ParseCounterAsync(getResp);
        counter!.Value!.Should().Be(1);
    }

    [Fact]
    public async Task GetCounter_NotFound_Returns404()
    {
        var resp = await _client.GetAsync($"/counters/does-not-exist-{Guid.NewGuid():N}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResetCounter_AfterIncrements_StartsFromZero()
    {
        var name = $"test-reset-{Guid.NewGuid():N}";
        await _client.PostAsync($"/counters/{name}/increment", null);
        await _client.PostAsync($"/counters/{name}/increment", null);

        var deleteResp = await _client.DeleteAsync($"/counters/{name}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // After reset, a new increment must return 1
        await _client.PostAsync($"/counters/{name}/increment", null);
        var counter = await GetCounterAsync(name);
        counter!.Value!.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private async Task<Counter?> ParseCounterAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<Counter>(JsonOptions);
        return json;
    }

    private async Task<Counter?> GetCounterAsync(string name)
    {
        var resp = await _client.GetAsync($"/counters/{name}");
        resp.EnsureSuccessStatusCode();
        return await ParseCounterAsync(resp);
    }
}
