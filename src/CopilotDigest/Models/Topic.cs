namespace CopilotDigest.Models;

public class Topic
{
    /// <summary>Short display name for the topic, e.g. "AI News".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional longer description used to provide context to the AI.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional custom prompt override.  When null, a default prompt is
    /// constructed from <see cref="Name"/> and <see cref="Description"/>.
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// Set to <see langword="true"/> by <c>FinancePromptEnricher</c> when the topic has
    /// direct stock holdings that required financial fundamentals, but no data was returned
    /// (e.g. Alpha Vantage API key missing, rate-limited, or returned no data).
    /// <c>ResearchService</c> uses this to send an error email rather than a misleading success.
    /// </summary>
    public bool FinancialDataMissing { get; set; }
}
