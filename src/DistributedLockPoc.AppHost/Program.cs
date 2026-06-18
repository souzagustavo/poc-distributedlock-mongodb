using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var username = builder.AddParameter("username", value: "admin", secret: true);
var password = builder.AddParameter("password", value: "123456", secret: true);

var mongo = builder.AddMongoDB("mongodb", userName: username, password: password)
    .WithMongoExpress();

var mongoDb = mongo.AddDatabase("distributedlockpoc");

builder.AddProject<Projects.DistributedLockPoc_Api>("api")
    .WithReference(mongoDb)
    .WaitFor(mongoDb);

builder.Build().Run();
