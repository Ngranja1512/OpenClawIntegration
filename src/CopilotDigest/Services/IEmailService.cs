using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>Sends topic summaries to a configured e-mail address.</summary>
public interface IEmailService
{
    Task SendSummariesAsync(IReadOnlyList<SummaryResult> summaries, CancellationToken cancellationToken = default);
}
