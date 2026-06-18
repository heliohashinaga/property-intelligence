var builder = DistributedApplication.CreateBuilder(args);

var postgresUsername = builder.AddParameter("postgres-username", "property_intelligence");
var postgresPassword = builder.AddParameter("postgres-password", "property_intelligence");

var postgres = builder.AddPostgres("postgres", postgresUsername, postgresPassword, port: 5432)
    .WithImage("postgis/postgis", "16-3.4");
var database = postgres.AddDatabase("property-intelligence");

builder.AddProject("api", "../PropertyIntelligence.Api/PropertyIntelligence.Api.csproj")
    .WithReference(database)
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["DATABASE_URL"] = "Host=localhost;Port=5432;Database=property-intelligence;Username=property_intelligence;Password=property_intelligence";
    })
    .WaitFor(database);

builder.Build().Run();
