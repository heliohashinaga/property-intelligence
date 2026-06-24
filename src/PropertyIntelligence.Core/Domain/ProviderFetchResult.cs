namespace PropertyIntelligence.Core.Domain;

/// <summary>
/// Result returned by an <c>IDataProvider&lt;T&gt;</c>, carrying both the transformed
/// domain payload and the original raw payload captured before transformation.
/// </summary>
/// <typeparam name="T">Typed provider payload used by the enrichment pipeline.</typeparam>
public sealed record ProviderFetchResult<T>
{
    /// <summary>Typed domain payload consumed by the enrichment pipeline.</summary>
    public required T Data { get; init; }

    /// <summary>
    /// Original provider payload serialized exactly as captured at the source
    /// boundary (HTTP body, file row/document, fixture JSON, etc.).
    /// </summary>
    public required string RawPayload { get; init; }
}
