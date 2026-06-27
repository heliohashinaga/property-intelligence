using NRules.Fluent.Dsl;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

/// <summary>
/// Scores the mobility dimension from transit access and nearby walkability proxies.
/// </summary>
public sealed class MobilityRules : Rule
{
    public override void Define()
    {
        PropertyFact property = default!;

        When()
            .Match<PropertyFact>(
                () => property,
                fact => fact.Profile.PoiData != null);

        Then()
            .Do(ctx => ctx.Insert(CreateScoringFact(property)));
    }

    private static ScoringFact CreateScoringFact(PropertyFact property)
    {
        var poiData = property.Profile.PoiData!;
        var score = Score(poiData);

        return new ScoringFact
        {
            Dimension = "mobility",
            Points = score.Points,
            Reason = score.Reason,
        };
    }

    private static MobilityScore Score(PoiData poiData)
    {
        var transitPoints = ScoreTransit(poiData);
        var amenityPoints = ScoreAmenities(poiData);
        var totalPoints = Math.Min(200, transitPoints + amenityPoints);

        var reason = $"mobility:transit500m={poiData.TransitStops500m};transit1km={poiData.TransitStops1km};pois2km={poiData.Pois2km};supermarkets1km={poiData.Supermarkets1km};pharmacies1km={poiData.Pharmacies1km};parks1km={poiData.Parks1km}";

        return new MobilityScore(totalPoints, reason);
    }

    private static int ScoreTransit(PoiData poiData)
    {
        if (poiData.TransitStops500m >= 10)
        {
            return 120;
        }

        if (poiData.TransitStops500m >= 5)
        {
            return 90;
        }

        if (poiData.TransitStops500m >= 1)
        {
            return 60;
        }

        if (poiData.TransitStops1km >= 5)
        {
            return 30;
        }

        if (poiData.TransitStops1km >= 1)
        {
            return 20;
        }

        return 0;
    }

    private static int ScoreAmenities(PoiData poiData)
    {
        var points = 0;

        if (poiData.Supermarkets1km >= 3)
        {
            points += 20;
        }
        else if (poiData.Supermarkets1km >= 1)
        {
            points += 10;
        }

        if (poiData.Pharmacies1km >= 3)
        {
            points += 20;
        }
        else if (poiData.Pharmacies1km >= 1)
        {
            points += 10;
        }

        if (poiData.Parks1km >= 1)
        {
            points += 10;
        }

        if (poiData.Pois2km >= 100)
        {
            points += 30;
        }
        else if (poiData.Pois2km >= 30)
        {
            points += 15;
        }

        return points;
    }

    private sealed record MobilityScore(int Points, string Reason);
}
