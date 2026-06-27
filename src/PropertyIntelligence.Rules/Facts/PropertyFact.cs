using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Rules.Facts;

/// <summary>
/// NRules fact wrapper for the enriched property profile.
/// </summary>
public sealed record PropertyFact
{
    public required PropertyProfile Profile { get; init; }
}
