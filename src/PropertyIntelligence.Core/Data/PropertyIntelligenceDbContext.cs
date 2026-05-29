using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Core.Data;

/// <summary>
/// EF Core DbContext for Property Intelligence.
/// Configured with Npgsql + NetTopologySuite for PostGIS geometry support.
/// Register as <c>services.AddDbContext&lt;PropertyIntelligenceDbContext&gt;(...)</c>.
/// </summary>
public sealed class PropertyIntelligenceDbContext : DbContext
{
    public PropertyIntelligenceDbContext(
        DbContextOptions<PropertyIntelligenceDbContext> options)
        : base(options) { }

    public DbSet<PropertyAddress>     PropertyAddresses     => Set<PropertyAddress>();
    public DbSet<PropertyAnalysis>    PropertyAnalyses      => Set<PropertyAnalysis>();
    public DbSet<ApiConsumer>         ApiConsumers          => Set<ApiConsumer>();
    public DbSet<DataProviderRawLog>  DataProviderRawLogs   => Set<DataProviderRawLog>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        // ── property_addresses ────────────────────────────────────────────
        mb.Entity<PropertyAddress>(e =>
        {
            e.ToTable("property_addresses");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id)
             .HasColumnType("uuid")
             .HasDefaultValueSql("gen_random_uuid()");

            e.Property(a => a.NormalizedAddress)
             .IsRequired()
             .HasColumnName("normalized_address");
            e.HasIndex(a => a.NormalizedAddress).IsUnique();

            e.Property(a => a.StreetName).HasColumnName("street_name");
            e.Property(a => a.StreetNumber).HasColumnName("street_number");
            e.Property(a => a.Neighborhood).HasColumnName("neighborhood");
            e.Property(a => a.City).IsRequired().HasColumnName("city");
            e.Property(a => a.State)
             .IsRequired()
             .HasColumnType("varchar(2)")
             .HasColumnName("state");
            e.Property(a => a.PostalCode)
             .HasColumnType("varchar(8)")
             .HasColumnName("postal_code");
            e.Property(a => a.Lat)
             .HasColumnType("numeric(9,6)")
             .HasColumnName("lat");
            e.Property(a => a.Lng)
             .HasColumnType("numeric(9,6)")
             .HasColumnName("lng");

            // PostGIS geometry column — managed by NetTopologySuite at query time;
            // the column itself is defined in the SQL migration.
            e.Ignore(a => a.Lat); // lat/lng stored as PostGIS geometry(Point,4326)
            e.Ignore(a => a.Lng); // raw doubles duplicated in the geometry column

            e.Property(a => a.CreatedAt)
             .HasColumnName("created_at")
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("NOW()");
        });

        // ── api_consumers ─────────────────────────────────────────────────
        mb.Entity<ApiConsumer>(e =>
        {
            e.ToTable("api_consumers");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id)
             .HasColumnType("uuid")
             .HasDefaultValueSql("gen_random_uuid()");

            e.Property(c => c.Name).IsRequired();
            e.Property(c => c.ApiKeyHash)
             .IsRequired()
             .HasColumnType("varchar(64)")
             .HasColumnName("api_key_hash");
            e.HasIndex(c => c.ApiKeyHash).IsUnique();

            e.Property(c => c.CreatedAt)
             .HasColumnName("created_at")
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("NOW()");
            e.Property(c => c.LastUsedAt)
             .HasColumnName("last_used_at")
             .HasColumnType("timestamptz");
            e.Property(c => c.IsActive)
             .HasColumnName("is_active")
             .HasDefaultValue(true);
        });

        // ── property_analyses (append-only) ───────────────────────────────
        mb.Entity<PropertyAnalysis>(e =>
        {
            e.ToTable("property_analyses");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id)
             .HasColumnType("uuid")
             .HasDefaultValueSql("gen_random_uuid()");

            e.Property(a => a.AddressId)
             .IsRequired()
             .HasColumnType("uuid")
             .HasColumnName("address_id");
            e.HasOne<PropertyAddress>()
             .WithMany()
             .HasForeignKey(a => a.AddressId)
             .OnDelete(DeleteBehavior.Restrict);

            e.Property(a => a.ApiConsumerId)
             .IsRequired()
             .HasColumnType("uuid")
             .HasColumnName("api_consumer_id");
            e.HasOne<ApiConsumer>()
             .WithMany()
             .HasForeignKey(a => a.ApiConsumerId)
             .OnDelete(DeleteBehavior.Restrict);

            e.Property(a => a.CompositeScore)
             .IsRequired()
             .HasColumnType("smallint")
             .HasColumnName("composite_score");
            e.Property(a => a.CompositeMax)
             .IsRequired()
             .HasColumnType("smallint")
             .HasColumnName("composite_max");
            e.Property(a => a.Grade)
             .IsRequired()
             .HasColumnType("varchar(2)");

            // JSONB columns — serialised with System.Text.Json
            e.Property(a => a.DimensionScores)
             .IsRequired()
             .HasColumnType("jsonb")
             .HasColumnName("dimension_scores")
             .HasConversion(
                 v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                 v => JsonSerializer.Deserialize<List<DimensionScore>>(v, (JsonSerializerOptions?)null)!);

            e.Property(a => a.Warnings)
             .IsRequired()
             .HasColumnType("jsonb")
             .HasConversion(
                 v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                 v => JsonSerializer.Deserialize<List<AnalysisWarning>>(v, (JsonSerializerOptions?)null)!);

            // text[] columns
            e.Property(a => a.RiskFlags)
             .IsRequired()
             .HasColumnType("text[]")
             .HasColumnName("risk_flags")
             .HasConversion(
                 v => v.ToArray(),
                 v => (IReadOnlyList<string>)v);

            e.Property(a => a.OpportunityFlags)
             .IsRequired()
             .HasColumnType("text[]")
             .HasColumnName("opportunity_flags")
             .HasConversion(
                 v => v.ToArray(),
                 v => (IReadOnlyList<string>)v);

            e.Property(a => a.ProvidersUsed)
             .IsRequired()
             .HasColumnType("text[]")
             .HasColumnName("providers_used")
             .HasConversion(
                 v => v.ToArray(),
                 v => (IReadOnlyList<string>)v);

            e.Property(a => a.ProvidersUnavailable)
             .IsRequired()
             .HasColumnType("text[]")
             .HasColumnName("providers_unavailable")
             .HasConversion(
                 v => v.ToArray(),
                 v => (IReadOnlyList<string>)v);

            e.Property(a => a.Insight).HasColumnName("insight");
            e.Property(a => a.InsightUnavailable)
             .IsRequired()
             .HasColumnName("insight_unavailable")
             .HasDefaultValue(false);
            e.Property(a => a.LlmModel)
             .IsRequired()
             .HasColumnType("varchar(100)")
             .HasColumnName("llm_model");
            e.Property(a => a.RulesVersion)
             .IsRequired()
             .HasColumnType("varchar(20)")
             .HasColumnName("rules_version");
            e.Property(a => a.Cached)
             .IsRequired()
             .HasColumnName("cached")
             .HasDefaultValue(false);
            e.Property(a => a.RequestIp)
             .HasColumnType("inet")
             .HasColumnName("request_ip");
            e.Property(a => a.CreatedAt)
             .IsRequired()
             .HasColumnType("timestamptz")
             .HasColumnName("created_at")
             .HasDefaultValueSql("NOW()");

            // Append-only constraint: disable update/delete tracking helpers
            e.ToTable(t => t.ExcludeFromMigrations(false));
        });

        // ── data_provider_raw_logs ────────────────────────────────────────
        mb.Entity<DataProviderRawLog>(e =>
        {
            e.ToTable("data_provider_raw_logs");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id)
             .HasColumnType("uuid")
             .HasDefaultValueSql("gen_random_uuid()");

            e.Property(l => l.AddressId)
             .IsRequired()
             .HasColumnType("uuid")
             .HasColumnName("address_id");
            e.HasOne<PropertyAddress>()
             .WithMany()
             .HasForeignKey(l => l.AddressId)
             .OnDelete(DeleteBehavior.Restrict);

            e.Property(l => l.AnalysisId)
             .HasColumnType("uuid")
             .HasColumnName("analysis_id");
            e.HasOne<PropertyAnalysis>()
             .WithMany()
             .HasForeignKey(l => l.AnalysisId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);

            e.Property(l => l.ProviderName)
             .IsRequired()
             .HasColumnName("provider_name");
            e.Property(l => l.RawPayload)
             .IsRequired()
             .HasColumnName("raw_payload");
            e.Property(l => l.FetchedAt)
             .IsRequired()
             .HasColumnType("timestamptz")
             .HasColumnName("fetched_at")
             .HasDefaultValueSql("NOW()");
        });
    }
}
