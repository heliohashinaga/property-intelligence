using Microsoft.EntityFrameworkCore;
using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Core.Data;

/// <summary>
/// EF Core DbContext for Property Intelligence.
/// NOTE: This is a compilation stub created by T012/T058 to unblock parallel work.
///       T009 will add full entity configurations, OnModelCreating mappings,
///       JSONB column setup, and all remaining DbSets.
/// </summary>
public class PropertyIntelligenceDbContext : DbContext
{
    public PropertyIntelligenceDbContext(DbContextOptions<PropertyIntelligenceDbContext> options)
        : base(options) { }

    // ── DbSets (T009 will add remaining sets and configure all mappings) ─────
    public DbSet<ApiConsumer>          ApiConsumers          { get; set; } = null!;
    public DbSet<PropertyAnalysis>     PropertyAnalyses      { get; set; } = null!;
    public DbSet<DataProviderRawLog>   DataProviderRawLogs   { get; set; } = null!;

    // property_addresses is a lookup/upsert target — added here for reference
    // T009 will configure the full entity mapping with PostGIS geometry.
}
