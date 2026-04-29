using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Calls the GitHub Copilot chat-completions endpoint
/// (<c>https://api.githubcopilot.com/chat/completions</c>) to generate
/// an AI-powered summary for a given topic.
/// </summary>
public class CopilotService : ICopilotService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Uri CopilotTokenExchangeUri =
        new("https://api.github.com/copilot_internal/v2/token");

    private readonly HttpClient _http;
    private readonly CopilotSettings _settings;
    private readonly ILogger<CopilotService> _logger;
    private readonly bool _usesCopilotProEndpoint;

    // Short-lived Copilot API token obtained via token exchange
    private string? _copilotApiToken;
    private DateTimeOffset _copilotApiTokenExpiry = DateTimeOffset.MinValue;

    public CopilotService(
        HttpClient http,
        IOptions<AppSettings> options,
        ILogger<CopilotService> logger)
    {
        _http = http;
        _settings = options.Value.Copilot;
        _logger = logger;

        _http.BaseAddress = new Uri(_settings.ApiUrl.TrimEnd('/') + "/");
        // GitHub token used as-is for GitHub Models, and for the token-exchange
        // request when targeting the Copilot Pro endpoint.
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.Token);
        _http.DefaultRequestHeaders.Add("Copilot-Integration-Id", "copilot-daily-digest");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "CopilotDigest/1.0");

        _usesCopilotProEndpoint = _settings.ApiUrl
            .Contains("api.githubcopilot.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the bearer token to use for API calls.
    /// For the Copilot Pro endpoint, exchanges the GitHub token for a
    /// short-lived Copilot API token (~30 min) and caches it until near-expiry.
    /// </summary>
    private async Task<string> GetAuthTokenAsync(CancellationToken ct)
    {
        if (!_usesCopilotProEndpoint)
            return _settings.Token;

        if (_copilotApiToken is not null && DateTimeOffset.UtcNow < _copilotApiTokenExpiry.AddMinutes(-5))
            return _copilotApiToken;

        _logger.LogDebug("Exchanging GitHub token for Copilot API token");

        // The DefaultRequestHeaders.Authorization already carries the GitHub token,
        // which is what this exchange endpoint requires.
        using var exchangeRequest = new HttpRequestMessage(HttpMethod.Get, CopilotTokenExchangeUri);
        using var exchangeResponse = await _http.SendAsync(exchangeRequest, ct);

        if (!exchangeResponse.IsSuccessStatusCode)
        {
            var body = await exchangeResponse.Content.ReadAsStringAsync(ct);
            _logger.LogError("Token exchange failed ({Status}): {Body}",
                (int)exchangeResponse.StatusCode, body);
        }

        exchangeResponse.EnsureSuccessStatusCode();

        var exchangeJson = await exchangeResponse.Content.ReadAsStringAsync(ct);
        var tokenData = JsonSerializer.Deserialize<CopilotTokenResponse>(exchangeJson, JsonOptions);

        _copilotApiToken = tokenData!.Token;
        _copilotApiTokenExpiry = tokenData.ExpiresAt;
        _logger.LogDebug("Copilot API token obtained, expires at {Expiry}", _copilotApiTokenExpiry);

        return _copilotApiToken;
    }

    public async Task<string> SummariseTopicAsync(Topic topic, CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(topic);
        var request = new CopilotChatRequest
        {
            Model = _settings.Model,
            MaxTokens = _settings.MaxTokens,
            Messages =
            [
                new CopilotMessage
                {
                    Role = "system",
                    Content = "You are a research assistant. Provide concise, " +
                              "informative summaries of the latest information on the given topic. " +
                              "Structure your response with bullet points for key takeaways.",
                },
                new CopilotMessage { Role = "user", Content = prompt },
            ],
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);

        _logger.LogInformation("Requesting Copilot summary for topic: {Topic}", topic.Name);

        var authToken = await GetAuthTokenAsync(cancellationToken);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Copilot API returned {StatusCode} for topic {Topic}. Response: {Body}",
                (int)response.StatusCode, topic.Name, errorBody);
        }

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<CopilotChatResponse>(responseJson, JsonOptions);

        var summary = chatResponse?.Choices.FirstOrDefault()?.Message.Content
                      ?? "No summary was returned by the Copilot API.";

        _logger.LogInformation("Received Copilot summary for topic: {Topic}", topic.Name);
        return summary;
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var authToken = await GetAuthTokenAsync(cancellationToken);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, "models");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var modelsResponse = JsonSerializer.Deserialize<CopilotModelsResponse>(json, JsonOptions);

        return modelsResponse?.Data.Select(m => m.Id).OrderBy(id => id).ToList()
               ?? [];
    }

    private static string BuildPrompt(Topic topic)
    {
        if (!string.IsNullOrWhiteSpace(topic.Prompt))
            return topic.Prompt;

        var sb = new StringBuilder();
        sb.Append($"Research and summarise the latest information about: {topic.Name}.");
        if (!string.IsNullOrWhiteSpace(topic.Description))
            sb.Append($" Context: {topic.Description}");
        sb.Append(" Focus on the most important developments from the past 24 hours.");
        return sb.ToString();
    }
}
