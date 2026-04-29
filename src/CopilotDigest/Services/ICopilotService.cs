using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Generates an AI-powered summary for a given topic using the
/// GitHub Copilot chat-completions API.
/// </summary>
public interface ICopilotService
{
    Task<string> SummariseTopicAsync(Topic topic, CancellationToken cancellationToken = default);

    /// <summary>Returns the model IDs accepted by the configured API endpoint.</summary>
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);
}
