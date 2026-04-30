using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Orchestrates topic research and returns a <see cref="SummaryResult"/>.
/// </summary>
public interface IResearchService
{
    /// <summary>
    /// Generates a summary for the given topic and returns the result.
    /// </summary>
    Task<SummaryResult> ResearchTopicAsync(Topic topic, CancellationToken cancellationToken = default);
}
