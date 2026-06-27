using NRules.Fluent.Dsl;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

/// <summary>
/// Scores the security dimension from crime incidents per 100k residents.
/// </summary>
public sealed class SecurityRules : Rule
{
    public override void Define()
    {
        PropertyFact property = default!;

        When()
            .Match<PropertyFact>(
                () => property,
                fact => fact.Profile.CrimeData != null,
                fact => fact.Profile.CrimeData!.CrimeRatePer100k.HasValue);

        Then()
            .Do(ctx => ctx.Insert(CreateScoringFact(property)));
    }

    private static ScoringFact CreateScoringFact(PropertyFact property)
    {
        var crimeRatePer100k = property.Profile.CrimeData!.CrimeRatePer100k!.Value;
        var score = Score(crimeRatePer100k);

        return new ScoringFact
        {
            Dimension = "security",
            Points = score.Points,
            Reason = score.Reason,
        };
    }

    private static SecurityScore Score(double crimeRatePer100k)
    {
        if (crimeRatePer100k < 50)
        {
            return new SecurityScore(180, "crime_rate_per_100k:<50");
        }

        if (crimeRatePer100k < 100)
        {
            return new SecurityScore(140, "crime_rate_per_100k:50-99");
        }

        if (crimeRatePer100k < 200)
        {
            return new SecurityScore(100, "crime_rate_per_100k:100-199");
        }

        if (crimeRatePer100k <= 300)
        {
            return new SecurityScore(60, "crime_rate_per_100k:200-300");
        }

        return new SecurityScore(20, "crime_rate_per_100k:>300");
    }

    private sealed record SecurityScore(int Points, string Reason);
}
