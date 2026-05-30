using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Explainability;

/// <summary>
/// Calls OpenRouter (OpenAI-compatible) to generate a PT-BR insight paragraph.
/// Best-effort: always returns null on failure — NEVER throws (Constitution §V).
/// Model selected via <c>LLM_MODEL</c> env var; falls back to a free model.
/// </summary>
public sealed class LlmExplainabilityService : IExplainabilityService
{
    private const string OpenRouterEndpoint = "https://openrouter.ai/api/v1/chat/completions";
    private const string FallbackModel      = "meta-llama/llama-3.1-8b-instruct:free";

    private readonly HttpClient    _http;
    private readonly string        _model;
    private readonly ILogger<LlmExplainabilityService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public LlmExplainabilityService(
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogger<LlmExplainabilityService> logger)
    {
        _http   = httpFactory.CreateClient("openrouter");
        _model  = configuration["LLM_MODEL"] ?? FallbackModel;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string?> GenerateInsightAsync(
        PropertyAnalysis analysis,
        PropertyProfile  profile,
        CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var prompt = BuildPrompt(analysis, profile);

            var requestBody = new
            {
                model    = _model,
                messages = new[]
                {
                    new { role = "system", content = "Você é um especialista em análise imobiliária brasileira. Responda sempre em português do Brasil. Seja objetivo, claro e baseado nos dados fornecidos." },
                    new { role = "user",   content = prompt }
                },
                max_tokens  = 400,
                temperature = 0.4,
            };

            using var response = await _http.PostAsJsonAsync(
                OpenRouterEndpoint, requestBody, JsonOpts, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OpenRouter returned {Status} for model {Model}",
                    response.StatusCode, _model);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonDocument>(
                cancellationToken: cts.Token);

            var content = json?.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("LLM insight generation timed out for analysis {Id}", analysis.Id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM insight generation failed for analysis {Id}", analysis.Id);
            return null;
        }
    }

    private static string BuildPrompt(PropertyAnalysis analysis, PropertyProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Analise o seguinte imóvel e gere um parágrafo de insight em PT-BR:");
        sb.AppendLine($"Endereço: {profile.Address.NormalizedAddress}");
        sb.AppendLine($"Score composto: {analysis.CompositeScore}/{analysis.CompositeMax} (Grade: {analysis.Grade})");
        sb.AppendLine();
        sb.AppendLine("Pontuações por dimensão:");

        foreach (var dim in analysis.DimensionScores)
        {
            if (dim.Status == DimensionStatus.Available)
                sb.AppendLine($"  - {dim.Dimension}: {dim.Score}/200 ({dim.Trend?.ToString().ToLower() ?? "stable"})");
            else
                sb.AppendLine($"  - {dim.Dimension}: indisponível");
        }

        if (analysis.RiskFlags.Count > 0)
            sb.AppendLine($"Flags de risco: {string.Join(", ", analysis.RiskFlags)}");

        if (analysis.OpportunityFlags.Count > 0)
            sb.AppendLine($"Oportunidades: {string.Join(", ", analysis.OpportunityFlags)}");

        if (analysis.ProvidersUnavailable.Count > 0)
            sb.AppendLine($"Providers indisponíveis: {string.Join(", ", analysis.ProvidersUnavailable)}");

        sb.AppendLine();
        sb.AppendLine("Gere um parágrafo conciso (máximo 3 frases) explicando os pontos mais relevantes para quem está considerando comprar ou alugar este imóvel.");

        return sb.ToString();
    }
}
