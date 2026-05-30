using System.Security.Cryptography;
using System.Text;

namespace PropertyIntelligence.Api.Logging;

/// <summary>
/// LGPD Art. 6º, VI — Transparência e minimização de dados pessoais em logs.
/// Endereços são dados pessoais (Lei 13.709/2018, Art. 5º, I) e NUNCA devem
/// aparecer em texto claro em logs de aplicação.
///
/// Use <see cref="Hash"/> para pseudonimizar endereços antes de logar.
/// O hash é determinístico (mesmo endereço → mesmo hash) para correlação de logs,
/// mas não é reversível sem a string original.
/// </summary>
public static class AddressLogEnricher
{
    /// <summary>
    /// Retorna os primeiros 16 caracteres hex do SHA-256 do endereço normalizado
    /// (lowercase, trimmed). Prefixado com <c>addr:</c> para identificação no log.
    /// </summary>
    /// <example>
    /// Input:  "Rua Augusta, 1500 - Consolação, São Paulo - SP"
    /// Output: "addr:3f8a2c1d9e74b056"
    /// </example>
    public static string Hash(string? rawAddress)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
            return "addr:[empty]";

        var normalized = rawAddress.ToLowerInvariant().Trim();
        var bytes      = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"addr:{Convert.ToHexString(bytes)[..16].ToLowerInvariant()}";
    }
}
