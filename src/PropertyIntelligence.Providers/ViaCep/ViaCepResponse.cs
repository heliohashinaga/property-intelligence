namespace PropertyIntelligence.Providers.ViaCep;

/// <summary>Internal DTO for ViaCEP API response deserialization.</summary>
internal sealed class ViaCepResponse
{
    public string? Cep         { get; init; }
    public string? Logradouro  { get; init; }
    public string? Complemento { get; init; }
    public string? Bairro      { get; init; }
    public string? Localidade  { get; init; }
    public string? Uf          { get; init; }
    public string? Ibge        { get; init; }
    public string? Erro      { get; init; }  // API returns string "true", not bool
}
