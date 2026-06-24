namespace PropertyIntelligence.Core.Domain;

/// <summary>
/// Registered API consumer. Persisted in <c>api_consumers</c>.
/// API key is stored only as a SHA-256 hex hash — never in plaintext.
/// </summary>
public sealed record ApiConsumer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }

    /// <summary>SHA-256 hex digest of the consumer's API key (64 chars).</summary>
    public required string ApiKeyHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; init; }
    public bool IsActive { get; init; } = true;
}
