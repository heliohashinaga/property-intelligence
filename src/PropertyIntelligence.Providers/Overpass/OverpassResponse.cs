using System.Text.Json.Serialization;

namespace PropertyIntelligence.Providers.Overpass;

/// <summary>Internal DTOs for Overpass API response deserialization.</summary>
internal sealed class OverpassResponse
{
    [JsonPropertyName("elements")]
    public List<OverpassElement> Elements { get; init; } = [];
}

internal sealed class OverpassElement
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("lat")]
    public double? Lat { get; init; }

    [JsonPropertyName("lon")]
    public double? Lon { get; init; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; init; } = [];

    [JsonPropertyName("center")]
    public OverpassCenter? Center { get; init; }
}

internal sealed class OverpassCenter
{
    [JsonPropertyName("lat")]
    public double Lat { get; init; }

    [JsonPropertyName("lon")]
    public double Lon { get; init; }
}
