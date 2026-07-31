var builder = DistributedApplication.CreateBuilder(args);

var postgresUsername = builder.AddParameter("postgres-username", "property_intelligence");
var postgresPassword = builder.AddParameter("postgres-password", "property_intelligence");

var postgres = builder.AddPostgres("postgres", postgresUsername, postgresPassword, port: 5432)
    .WithImage("postgis/postgis", "16-3.4");
var database = postgres.AddDatabase("property-intelligence");

var redis = builder.AddRedis("redis", port: 6379);

builder.AddProject("api", "../PropertyIntelligence.Api/PropertyIntelligence.Api.csproj")
    .WithReference(database)
    .WithReference(redis)
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["DATABASE_URL"] = "Host=localhost;Port=5432;Database=property-intelligence;Username=property_intelligence;******";
    })
    .WaitFor(database)
    .WaitFor(redis);

builder.Build().Run();
