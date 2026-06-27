using Microsoft.Extensions.Logging;
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
    private readonly ILogger<EfCoreDataProviderRawLogStore> _logger;

    public EfCoreDataProviderRawLogStore(
        PropertyIntelligenceDbContext dbContext,
        ILogger<EfCoreDataProviderRawLogStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SaveAsync(IReadOnlyList<DataProviderRawLog> logs, CancellationToken ct = default)
    {
        if (logs.Count == 0)
        {
            return;
        }

        try
        {
            _dbContext.DataProviderRawLogs.AddRange(logs);
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist {Count} provider raw logs. Continuing request without blocking analysis.",
                logs.Count);
        }
    }
}
