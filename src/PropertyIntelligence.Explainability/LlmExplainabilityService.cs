using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropertyIntelligence.Core.Domain;
using PropertyIntelligence.Core.Interfaces;

namespace PropertyIntelligence.Explainability;

/// <summary>
/// Best-effort explainability service backed by OpenRouter's OpenAI-compatible chat completions API.
/// Returns <c>null</c> on timeout/failure to avoid failing the analysis request.
/// </summary>
public sealed class LlmExplainabilityService : IExplainabilityService
{
    private const string OpenRouterChatCompletionsEndpoint = "https://openrouter.ai/api/v1/chat/completions";
    private const string HttpReferer = "https://property-intelligence.hashinaga.dev";
    private const string AppTitle = "Property Intelligence";
    private const string FallbackModel = "anthropic/claude-3-haiku";

    private readonly HttpClient _httpClient;
    private readonly ILogger<LlmExplainabilityService> _logger;
    private readonly TimeSpan _requestTimeout;

    public LlmExplainabilityService(
        HttpClient httpClient,
        ILogger<LlmExplainabilityService> logger,
        TimeSpan? requestTimeout = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task<string?> GenerateInsightAsync(
        PropertyAnalysis analysis,
        PropertyProfile profile,
        CancellationToken ct = default)
    {
        var correlationId = Activity.Current?.TraceId.ToString() ?? "unknown";
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "OpenRouter explainability skipped due to missing OPENROUTER_API_KEY. CorrelationId={CorrelationId}",
                correlationId);
            return null;
        }

        var model = ResolveModel(analysis);
        var body = BuildRequestBody(model, analysis, profile);
        var json = JsonSerializer.Serialize(body);

        using var request = new HttpRequestMessage(HttpMethod.Post, OpenRouterChatCompletionsEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("HTTP-Referer", HttpReferer);
        request.Headers.Add("X-Title", AppTitle);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_requestTimeout);

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OpenRouter explainability failed with status {StatusCode}. CorrelationId={CorrelationId}",
                    (int)response.StatusCode,
                    correlationId);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var jsonDocument = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);

            var content = TryExtractAssistantContent(jsonDocument.RootElement);
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning(
                    "OpenRouter explainability returned an empty content payload. CorrelationId={CorrelationId}",
                    correlationId);
                return null;
            }

            return content.Trim();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "OpenRouter explainability timed out after {TimeoutMs}ms. CorrelationId={CorrelationId}",
                _requestTimeout.TotalMilliseconds,
                correlationId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "OpenRouter explainability failed. CorrelationId={CorrelationId}",
                correlationId);
            return null;
        }
    }

    private static string ResolveModel(PropertyAnalysis analysis)
        => Environment.GetEnvironmentVariable("LLM_MODEL")
           ?? analysis.LlmModel
           ?? "meta-llama/llama-3.1-8b-instruct:free";

    private static object BuildRequestBody(string model, PropertyAnalysis analysis, PropertyProfile profile)
    {
        var systemMessage =
            "Você é um analista imobiliário brasileiro. Responda em PT-BR claro, direto e objetivo. " +
            "Explique score, riscos e oportunidades sem inventar dados.";

        var dimensions = string.Join(
            "\n",
            analysis.DimensionScores.Select(score =>
                $"- {score.Dimension}: score={score.Score?.ToString() ?? "null"}/{score.Max}, trend={score.Trend?.ToString().ToLowerInvariant() ?? "null"}, status={score.Status.ToString().ToLowerInvariant()}"));

        var riskFlags = analysis.RiskFlags.Count > 0 ? string.Join(", ", analysis.RiskFlags) : "nenhuma";
        var opportunityFlags = analysis.OpportunityFlags.Count > 0 ? string.Join(", ", analysis.OpportunityFlags) : "nenhuma";
        var unavailableProviders = analysis.ProvidersUnavailable.Count > 0 ? string.Join(", ", analysis.ProvidersUnavailable) : "nenhum";

        var userMessage =
            "Gere 1 parágrafo em PT-BR (máx. 120 palavras) com explicação do resultado abaixo.\n" +
            $"Endereço: {profile.Address.NormalizedAddress}\n" +
            $"Score: {analysis.CompositeScore}/{analysis.CompositeMax} ({analysis.Grade})\n" +
            "Dimensões:\n" +
            $"{dimensions}\n" +
            $"Riscos: {riskFlags}\n" +
            $"Oportunidades: {opportunityFlags}\n" +
            $"Providers indisponíveis: {unavailableProviders}\n" +
            "Inclua pelo menos 1 risco e 1 oportunidade quando disponíveis.";

        var models = new[] { model, FallbackModel }.Distinct(StringComparer.Ordinal).ToArray();

        return new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemMessage },
                new { role = "user", content = userMessage },
            },
            models,
            route = "fallback",
        };
    }

    private static string? TryExtractAssistantContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var contentElement))
        {
            return null;
        }

        return contentElement.ValueKind switch
        {
            JsonValueKind.String => contentElement.GetString(),
            JsonValueKind.Array => string.Join(
                "",
                contentElement
                    .EnumerateArray()
                    .Select(item => item.TryGetProperty("text", out var text) ? text.GetString() : null)
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            _ => null,
        };
    }
}
