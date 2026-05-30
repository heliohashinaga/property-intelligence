namespace PropertyIntelligence.Providers.Shared;

/// <summary>
/// Thrown by a provider when a hard failure occurs (e.g., CEP not found, auth error).
/// Soft failures (timeouts, 5xx) should be caught by the caller and recorded as unavailable.
/// </parameter name="content">
public sealed class ProviderException(string providerName, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The provider that raised this exception.</summary>
    public string ProviderName { get; } = providerName;
}
