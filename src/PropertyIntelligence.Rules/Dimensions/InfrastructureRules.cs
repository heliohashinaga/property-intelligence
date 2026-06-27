using NRules.Fluent.Dsl;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Rules.Facts;

namespace PropertyIntelligence.Rules.Dimensions;

/// <summary>
/// Scores the infrastructure dimension from nearby health and school signals.
/// </summary>
public sealed class InfrastructureRules : Rule
{
    public override void Define()
    {
        PropertyFact property = default!;

        When()
            .Match<PropertyFact>(
                () => property,
                fact => fact.Profile.HealthData != null || fact.Profile.SchoolData != null);

        Then()
            .Do(ctx => ctx.Insert(CreateScoringFact(property)));
    }

    private static ScoringFact CreateScoringFact(PropertyFact property)
    {
        var score = Score(property.Profile.HealthData, property.Profile.SchoolData);

        return new ScoringFact
        {
            Dimension = "infrastructure",
            Points = score.Points,
            Reason = score.Reason,
        };
    }

    private static InfrastructureScore Score(HealthData? healthData, SchoolData? schoolData)
    {
        var hospitalsWithin2km = healthData?.HospitalsWithin2km ?? 0;
        var clinicsWith2km = healthData?.ClinicsWith2km ?? 0;
        var emergencyUnits2km = healthData?.EmergencyUnits2km ?? 0;
        var nearestSchoolIdeb = schoolData?.NearestSchoolIdeb;

        var points = 0;

        if (hospitalsWithin2km >= 1)
        {
            points += 100;
        }

        if (clinicsWith2km >= 3)
        {
            points += 30;
        }
        else if (clinicsWith2km >= 1)
        {
            points += 15;
        }

        if (emergencyUnits2km >= 1)
        {
            points += 30;
        }

        if (nearestSchoolIdeb is >= 7.0)
        {
            points += 40;
        }
        else if (nearestSchoolIdeb is >= 6.0)
        {
            points += 20;
        }

        points = Math.Min(200, points);

        var reason = $"infrastructure:hospitals2km={hospitalsWithin2km};clinics2km={clinicsWith2km};emergency2km={emergencyUnits2km};nearestSchoolIdeb={(nearestSchoolIdeb?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "null")}";

        return new InfrastructureScore(points, reason);
    }

    private sealed record InfrastructureScore(int Points, string Reason);
}
