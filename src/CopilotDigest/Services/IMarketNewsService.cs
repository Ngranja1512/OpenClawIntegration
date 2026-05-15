using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Fetches recent macro and market news headlines from RSS feeds.
/// </summary>
public interface IMarketNewsService
{
    Task<IReadOnlyList<NewsItem>> GetMacroNewsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the most recent headlines for a single stock ticker from Yahoo Finance RSS.
    /// </summary>
    Task<IReadOnlyList<NewsItem>> GetStockNewsAsync(
        string yahooSymbol,
        int maxItems,
        CancellationToken cancellationToken = default);
}
