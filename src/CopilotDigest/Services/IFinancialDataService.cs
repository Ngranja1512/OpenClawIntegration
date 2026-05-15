using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Fetches fundamental financial data (revenue, margins, cash flow, valuation ratios)
/// for a given Yahoo Finance symbol.
/// </summary>
public interface IFinancialDataService
{
    Task<FinancialSnapshot?> GetFinancialSnapshotAsync(
        string yahooSymbol,
        CancellationToken cancellationToken = default);
}
