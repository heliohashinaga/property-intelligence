namespace PropertyIntelligence.Rules.Facts;

/// <summary>
/// NRules output fact representing dimension points and the reason produced by a rule.
/// </summary>
public sealed record ScoringFact
{
    public required string Dimension { get; init; }
    public required int Points { get; init; }
    public required string Reason { get; init; }
}
