// AppHost.cs — .NET Aspire orchestrator for Property Intelligence (local dev)
// Starts: PostgreSQL+PostGIS, Redis, API
// Dashboard: http://localhost:15000

var builder = DistributedApplication.CreateBuilder(args);

// ── PostgreSQL + PostGIS ──────────────────────────────────────────────────────
// Override the default postgres image with postgis/postgis for geospatial support.
// Aspire assigns a random host port to avoid conflicts with any existing container.
var postgres = builder
    .AddPostgres("postgres")
    .WithImage("postgis/postgis", "16-3.4")
    .WithEnvironment("POSTGRES_DB", "property_intelligence")
    .WithDataVolume("property-intelligence-postgres-data")
    .WithHostPort(5432)             // fixed port — migrations & import scripts depend on it
    .WithPgAdmin();                 // optional pgAdmin UI on a random port

var db = postgres.AddDatabase("property-intelligence-db", "property_intelligence");

// ── Redis ─────────────────────────────────────────────────────────────────────
var redis = builder
    .AddRedis("redis")
    .WithDataVolume("property-intelligence-redis-data")
    .WithHostPort(6379);            // fixed port — consistent with .env.example

// ── API ───────────────────────────────────────────────────────────────────────
builder
    .AddProject<Projects.PropertyIntelligence_Api>("api")
    .WithReference(db)
    .WithReference(redis)
    .WaitFor(db)
    .WaitFor(redis)
    .WithEnvironment("OPENROUTER_API_KEY",
        builder.Configuration["OPENROUTER_API_KEY"] ?? "changeme")
    .WithEnvironment("LLM_MODEL",
        builder.Configuration["LLM_MODEL"] ?? "meta-llama/llama-3.1-8b-instruct:free")
    .WithEnvironment("IPTU_API_KEY",
        builder.Configuration["IPTU_API_KEY"] ?? "changeme")
    .WithEnvironment("API_KEY_SALT",
        builder.Configuration["API_KEY_SALT"] ?? "dev-salt-32-chars-placeholder-ok");

builder.Build().Run();
