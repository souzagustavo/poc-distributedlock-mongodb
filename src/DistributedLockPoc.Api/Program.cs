using DistributedLockPoc.Api.Endpoints;
using DistributedLockPoc.Api.Models;
using DistributedLockPoc.Api.Services;
using Medallion.Threading;
using Medallion.Threading.MongoDB;
using MongoDB.Driver;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

const string databaseName = "distributedlockpoc";

builder.AddMongoDBClient(databaseName);

builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoClient>()
        .GetDatabase(databaseName)
        .GetCollection<Counter>("counters"));

builder.Services.AddHealthChecks()
    .AddMongoDb(
        clientFactory: f => f.GetRequiredService<IMongoClient>(),
        databaseNameFactory: _ => databaseName,
        name: "mongodb",
        tags: ["mongodb", "database"]);

builder.Services.AddScoped<IDistributedLockProvider>(sp =>
{
    var db = sp.GetRequiredService<IMongoDatabase>();
    return new MongoDistributedSynchronizationProvider(db, options =>
    {
        options.Expiry(TimeSpan.FromSeconds(30));
        // options.ExtensionCadence = Expiry / 3 Default
        options.BusyWaitSleepTime(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2));
    });
});

builder.Services.AddScoped<ICounterService, CounterService>();

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "Distributed Lock POC";
        options.Theme = Scalar.AspNetCore.ScalarTheme.Mars;
    });
}

app.MapCounterEndpoints();

app.Run();

// Make Program accessible for integration tests
public partial class Program { }
