using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Fetches fundamental financial data from Yahoo Finance's quoteSummary endpoint.
/// Modules used:
///   - financialData          → margins, FCF, cash, debt, revenue growth
///   - defaultKeyStatistics   → trailing/forward P/E, EPS, market cap
///   - incomeStatementHistory → last 4 years of revenue + net income
/// No API key required — uses the same public endpoint as the Yahoo Finance website.
/// </summary>
public sealed class YahooFinanceQuoteSummaryService : IFinancialDataService
{
    private readonly HttpClient              _http;
    private readonly FinancialDataSettings   _settings;
    private readonly ILogger<YahooFinanceQuoteSummaryService> _logger;

    public YahooFinanceQuoteSummaryService(
        HttpClient http,
        IOptions<AppSettings> options,
        ILogger<YahooFinanceQuoteSummaryService> logger)
    {
        _http     = http;
        _settings = options.Value.FinancialData;
        _logger   = logger;

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CopilotDigest/1.0");
        }
    }

    public async Task<FinancialSnapshot?> GetFinancialSnapshotAsync(
        string yahooSymbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yahooSymbol);

        if (!_settings.Enabled)
        {
            return null;
        }

        try
        {
            var baseUrl = _settings.QuoteSummaryUrl.TrimEnd('/');
            var url = $"{baseUrl}/{Uri.EscapeDataString(yahooSymbol)}" +
                      "?modules=financialData%2CdefaultKeyStatistics%2CincomeStatementHistory" +
                      "&corsDomain=finance.yahoo.com";

            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Yahoo quoteSummary returned {Status} for {Symbol}",
                    (int)response.StatusCode, yahooSymbol);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("quoteSummary", out var qs) ||
                !qs.TryGetProperty("result", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                return null;
            }

            var result = results[0];

            result.TryGetProperty("financialData",        out var fd);
            result.TryGetProperty("defaultKeyStatistics", out var dks);
            result.TryGetProperty("incomeStatementHistory", out var ish);

            return new FinancialSnapshot
            {
                Ticker            = yahooSymbol,
                MarketCap         = TryGetRaw(dks, "enterpriseValue")        // use enterprise value as proxy
                                    ?? TryGetRaw(dks, "marketCap"),
                TrailingPE        = TryGetRaw(dks, "trailingPE"),
                ForwardPE         = TryGetRaw(dks, "forwardPE"),
                TrailingEps       = TryGetRaw(dks, "trailingEps"),
                ForwardEps        = TryGetRaw(dks, "forwardEps"),
                RevenueTTM        = TryGetRaw(fd,  "totalRevenue"),
                RevenueGrowthYoY  = TryGetRaw(fd,  "revenueGrowth"),
                GrossMargins      = TryGetRaw(fd,  "grossMargins"),
                OperatingMargins  = TryGetRaw(fd,  "operatingMargins"),
                ProfitMargins     = TryGetRaw(fd,  "profitMargins"),
                FreeCashflow      = TryGetRaw(fd,  "freeCashflow"),
                TotalCash         = TryGetRaw(fd,  "totalCash"),
                TotalDebt         = TryGetRaw(fd,  "totalDebt"),
                AnnualHistory     = ParseAnnualHistory(ish),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch financial data for {Symbol}", yahooSymbol);
            return null;
        }
    }

    private static IReadOnlyList<AnnualFinancials> ParseAnnualHistory(JsonElement ish)
    {
        if (ish.ValueKind != JsonValueKind.Object ||
            !ish.TryGetProperty("incomeStatementHistory", out var stmts) ||
            stmts.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<AnnualFinancials>();

        foreach (var stmt in stmts.EnumerateArray())
        {
            // endDate is a Unix timestamp inside a { raw, fmt } object.
            DateTimeOffset? endDate = null;
            if (stmt.TryGetProperty("endDate", out var endDateEl) &&
                endDateEl.TryGetProperty("raw", out var rawTs) &&
                rawTs.TryGetInt64(out var ts))
            {
                endDate = DateTimeOffset.FromUnixTimeSeconds(ts);
            }

            if (endDate is null)
            {
                continue;
            }

            var revenue    = TryGetRaw(stmt, "totalRevenue");
            var netIncome  = TryGetRaw(stmt, "netIncome");

            results.Add(new AnnualFinancials(endDate.Value.Year, revenue, netIncome));
        }

        // Oldest first so the model reads the trend correctly.
        results.Sort((a, b) => a.Year.CompareTo(b.Year));
        return results;
    }

    /// <summary>
    /// Yahoo wraps every fundamental value in { "raw": 123.45, "fmt": "123.45" }.
    /// This helper reads the "raw" numeric value from a named field.
    /// Works both when <paramref name="parent"/> is the module root
    /// (e.g. financialData) and when it is an individual statement object.
    /// </summary>
    private static decimal? TryGetRaw(JsonElement parent, string fieldName)
    {
        if (parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!parent.TryGetProperty(fieldName, out var field))
        {
            return null;
        }

        // Some fields are plain numbers; others are wrapped objects.
        if (field.ValueKind == JsonValueKind.Number)
        {
            return field.TryGetDecimal(out var direct) ? direct : null;
        }

        if (field.ValueKind == JsonValueKind.Object &&
            field.TryGetProperty("raw", out var raw) &&
            raw.ValueKind == JsonValueKind.Number)
        {
            return raw.TryGetDecimal(out var wrapped) ? wrapped : null;
        }

        return null;
    }
}
