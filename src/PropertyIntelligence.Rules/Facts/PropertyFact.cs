namespace PropertyIntelligence.Rules.Facts;

/// <summary>Fact inserted into the NRules session. Wraps the full enriched property profile.</summary>
public sealed class PropertyFact
{
    public required PropertyIntelligence.Core.Domain.PropertyProfile Profile { get; init; }
}
