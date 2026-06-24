using PropertyIntelligence.Core.Data;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Core.Services;

/// <summary>
/// EF Core-backed implementation that persists raw provider payloads in
/// <c>data_provider_raw_logs</c>.
/// </summary>
public sealed class EfCoreDataProviderRawLogStore : IDataProviderRawLogStore
{
    private readonly PropertyIntelligenceDbContext _dbContext;

    public EfCoreDataProviderRawLogStore(PropertyIntelligenceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveAsync(IReadOnlyList<DataProviderRawLog> logs, CancellationToken ct = default)
    {
        if (logs.Count == 0)
        {
            return;
        }

        _dbContext.DataProviderRawLogs.AddRange(logs);
        await _dbContext.SaveChangesAsync(ct);
    }
}
