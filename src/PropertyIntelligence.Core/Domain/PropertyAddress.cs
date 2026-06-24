namespace PropertyIntelligence.Core.Domain;

/// <summary>
/// Structured representation of a Brazilian property address.
/// Created by <see cref="Interfaces.IAddressNormalizer"/> and persisted in <c>property_addresses</c>.
/// </summary>
public sealed record PropertyAddress
{
    /// <summary>Database primary key. Generated on first persist.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Full normalized address string, e.g. "Rua Augusta, 1500 - Consolação, São Paulo - SP, 01304-001".</summary>
    public required string NormalizedAddress { get; init; }

    public string? StreetName { get; init; }
    public string? StreetNumber { get; init; }
    public string? Neighborhood { get; init; }

    /// <summary>Municipality name, e.g. "São Paulo".</summary>
    public required string City { get; init; }

    /// <summary>Two-letter state code, e.g. "SP".</summary>
    public required string State { get; init; }

    /// <summary>CEP without hyphen, 8 digits, e.g. "01304001".</summary>
    public string? PostalCode { get; init; }

    /// <summary>WGS84 latitude. Populated after geocoding.</summary>
    public double? Lat { get; init; }

    /// <summary>WGS84 longitude. Populated after geocoding.</summary>
    public double? Lng { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
