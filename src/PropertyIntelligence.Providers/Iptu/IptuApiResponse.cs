using System.Text.Json.Serialization;

namespace PropertyIntelligence.Providers.Iptu;

/// <summary>Internal DTO for IPTU API response deserialization.</summary>
internal sealed class IptuApiResponse
{
    [JsonPropertyName("valor_venal")]
    public decimal? ValorVenal { get; init; }

    [JsonPropertyName("zoneamento")]
    public string? Zoneamento { get; init; }

    [JsonPropertyName("historico")]
    public List<IptuHistoricalEntry> Historico { get; init; } = [];
}

internal sealed class IptuHistoricalEntry
{
    [JsonPropertyName("ano")]
    public int Year { get; init; }

    [JsonPropertyName("valor_venal")]
    public decimal ValorVenal { get; init; }
}
