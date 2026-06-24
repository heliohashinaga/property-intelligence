namespace PropertyIntelligence.Core.Domain;

/// <summary>
/// Raw payload log for every provider fetch.
/// The original HTTP/file response MUST be stored here before any transformation,
/// so it is recoverable if a provider returns bad data (Constitution §II).
/// Persisted in <c>data_provider_raw_logs</c>.
/// </summary>
public sealed record DataProviderRawLog
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid AddressId { get; init; }

    /// <summary>
    /// FK to <c>property_analyses.id</c>.
    /// Nullable because logs are written BEFORE the analysis ID is known;
    /// backfilled in the same DB transaction after the analysis row is inserted.
    /// </summary>
    public Guid? AnalysisId { get; init; }

    /// <summary>Provider identifier matching <see cref="Interfaces.IDataProvider{TResult}.ProviderName"/>.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Serialized raw payload (JSON string). Never transformed or truncated.</summary>
    public required string RawPayload { get; init; }

    public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.UtcNow;
}
