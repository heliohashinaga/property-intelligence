using NRules.Fluent.Dsl;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

/// <summary>
/// Scores the urban context dimension from census income group and population density.
/// </summary>
public sealed class UrbanContextRules : Rule
{
    public override void Define()
    {
        PropertyFact property = default!;

        When()
            .Match<PropertyFact>(
                () => property,
                fact => fact.Profile.CensusData != null);

        Then()
            .Do(ctx => ctx.Insert(CreateScoringFact(property)));
    }

    private static ScoringFact CreateScoringFact(PropertyFact property)
    {
        var censusData = property.Profile.CensusData!;
        var score = Score(censusData);

        return new ScoringFact
        {
            Dimension = "urban_context",
            Points = score.Points,
            Reason = score.Reason,
        };
    }

    private static UrbanContextScore Score(CensusData censusData)
    {
        var incomePoints = ScoreIncome(censusData.MedianIncomeGroup);
        var densityPoints = ScoreDensity(censusData.PopulationDensity);
        var totalPoints = Math.Min(200, incomePoints + densityPoints);

        var reason = $"urban_context:incomeGroup={(censusData.MedianIncomeGroup?.ToString() ?? "null")};populationDensity={(censusData.PopulationDensity?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "null")}";

        return new UrbanContextScore(totalPoints, reason);
    }

    private static int ScoreIncome(int? medianIncomeGroup)
    {
        if (!medianIncomeGroup.HasValue)
        {
            return 0;
        }

        return medianIncomeGroup.Value switch
        {
            >= 9 => 140,
            8 => 130,
            7 => 115,
            6 => 95,
            5 => 75,
            4 => 55,
            3 => 40,
            2 => 25,
            _ => 10,
        };
    }

    private static int ScoreDensity(double? populationDensity)
    {
        if (!populationDensity.HasValue)
        {
            return 0;
        }

        var density = populationDensity.Value;

        if (density is >= 6000 and <= 12000)
        {
            return 60;
        }

        if (density is >= 4000 and < 6000 or > 12000 and <= 16000)
        {
            return 40;
        }

        if (density is >= 2000 and < 4000 or > 16000 and <= 22000)
        {
            return 20;
        }

        return 10;
    }

    private sealed record UrbanContextScore(int Points, string Reason);
}
