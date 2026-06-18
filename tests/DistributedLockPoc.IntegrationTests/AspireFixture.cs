using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DistributedLockPoc.IntegrationTests;

/// <summary>
/// Boots the full Aspire AppHost (MongoDB + API) once per test collection.
/// All tests share a single running instance — fast and realistic.
/// </summary>
public class AspireFixture : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private IDistributedApplicationTestingBuilder? _builder;
    public DistributedApplication? App { get; private set; }
    public HttpClient? ApiClient { get; private set; }

    public async Task InitializeAsync()
    {
        var cancellationToken = new CancellationTokenSource(DefaultTimeout).Token;

        _builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.DistributedLockPoc_AppHost>(cancellationToken);

        _builder.Services.ConfigureHttpClientDefaults(opts =>
            opts.AddStandardResilienceHandler());

        App = await _builder.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await App.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Wait until the API is healthy
        ApiClient = App.CreateHttpClient("api");
    }

    public async Task DisposeAsync()
    {
        if (App is not null)
            await App.DisposeAsync();
    }
}

[CollectionDefinition("Aspire")]
public class AspireCollection : ICollectionFixture<AspireFixture> { }
