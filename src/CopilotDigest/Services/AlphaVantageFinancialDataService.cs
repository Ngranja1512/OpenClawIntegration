using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Fetches fundamental financial data from Alpha Vantage — works from cloud/CI runners
/// unlike Yahoo Finance, which blocks AWS/Azure IP ranges.
///
/// Three API calls per symbol (free tier: 25 req/day):
///   1. OVERVIEW          → market cap, PE ratios, EPS, margins, analyst ratings
///   2. INCOME_STATEMENT  → annual revenue + net income history (last 4 years)
///   3. CASH_FLOW         → free cash flow, operating cash flow, capex, total cash
///
/// All Alpha Vantage fields are returned as strings; numeric "None" values are treated as null.
/// Requires a free API key from https://www.alphavantage.co/support/#api-key
/// </summary>
public sealed class AlphaVantageFinancialDataService : IFinancialDataService
{
    private readonly HttpClient           _http;
    private readonly AlphaVantageSettings _settings;
    private readonly FinancialDataSettings _financialSettings;
    private readonly ILogger<AlphaVantageFinancialDataService> _logger;

    public AlphaVantageFinancialDataService(
        HttpClient http,
        IOptions<AppSettings> options,
        ILogger<AlphaVantageFinancialDataService> logger)
    {
        _http              = http;
        _settings          = options.Value.AlphaVantage;
        _financialSettings = options.Value.FinancialData;
        _logger            = logger;

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CopilotDigest/1.0");
        }

        if (_financialSettings.Enabled && string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogError(
                "FinancialData is enabled but CopilotDigest:AlphaVantage:ApiKey is not configured. " +
                "Financial Fundamentals will be absent from all reports. " +
                "In GitHub Actions: add ALPHA_VANTAGE_KEY as a repository secret. " +
                "Locally: add ApiKey to appsettings.Local.json.");
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public async Task<FinancialSnapshot?> GetFinancialSnapshotAsync(
        string yahooSymbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yahooSymbol);

        if (!_financialSettings.Enabled)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogWarning(
                "Alpha Vantage API key is not configured — financial data unavailable for {Symbol}. " +
                "Set CopilotDigest__AlphaVantage__ApiKey (or appsettings.json AlphaVantage.ApiKey) " +
                "to a free key from https://www.alphavantage.co/support/#api-key",
                yahooSymbol);
            return null;
        }

        // Alpha Vantage uses plain US ticker symbols; strip any Yahoo suffix (.L, -B, etc.)
        var symbol = NormaliseSymbol(yahooSymbol);

        try
        {
            // Run the four fetches concurrently — they are independent.
            var overviewTask         = FetchJsonAsync($"function=OVERVIEW&symbol={Uri.EscapeDataString(symbol)}", cancellationToken);
            var incomeStatementTask  = FetchJsonAsync($"function=INCOME_STATEMENT&symbol={Uri.EscapeDataString(symbol)}", cancellationToken);
            var cashFlowTask         = FetchJsonAsync($"function=CASH_FLOW&symbol={Uri.EscapeDataString(symbol)}", cancellationToken);
            var balanceSheetTask     = FetchJsonAsync($"function=BALANCE_SHEET&symbol={Uri.EscapeDataString(symbol)}", cancellationToken);

            await Task.WhenAll(overviewTask, incomeStatementTask, cashFlowTask, balanceSheetTask);

            using var overview        = await overviewTask;
            using var incomeStatement = await incomeStatementTask;
            using var cashFlow        = await cashFlowTask;
            using var balanceSheet    = await balanceSheetTask;

            if (overview is null)
            {
                _logger.LogError("Alpha Vantage OVERVIEW returned no data for {Symbol}", symbol);
                return null;
            }

            var root = overview.RootElement;

            // AV returns { "Information": "..." } when the API key is invalid or the free-tier
            // premium endpoint is accessed without a paid key.
            if (root.TryGetProperty("Information", out var info))
            {
                _logger.LogError(
                    "Alpha Vantage API key error for {Symbol}: {Message}",
                    symbol, info.GetString());
                return null;
            }

            // AV returns { "Note": "..." } when the per-minute or per-day rate limit is hit.
            if (root.TryGetProperty("Note", out var note))
            {
                _logger.LogError(
                    "Alpha Vantage rate limit reached for {Symbol}: {Message}. " +
                    "Free tier allows 25 requests/day and 5/minute.",
                    symbol, note.GetString());
                return null;
            }

            // AV returns an empty object (all fields empty strings) for unknown symbols.
            // The "Symbol" field is empty when the ticker is not recognised.
            var symbolInResponse = ParseAvString(root, "Symbol");
            if (string.IsNullOrWhiteSpace(symbolInResponse))
            {
                _logger.LogError(
                    "Alpha Vantage did not recognise ticker '{Symbol}'. " +
                    "Make sure you are using the correct exchange ticker (e.g. 'AMZN' for Amazon, 'GOOGL' for Alphabet). " +
                    "You can look up the exact symbol at https://finance.yahoo.com or https://www.alphavantage.co/",
                    symbol);
                return null;
            }

            // OVERVIEW fields
            var marketCap        = ParseAvDecimal(root, "MarketCapitalization");
            var trailingPE       = ParseAvDecimal(root, "PERatio");
            var forwardPE        = ParseAvDecimal(root, "ForwardPE");
            var trailingEps      = ParseAvDecimal(root, "EPS");
            var operatingMargins = ParseAvDecimal(root, "OperatingMarginTTM");
            var profitMargins    = ParseAvDecimal(root, "ProfitMargin");
            var revGrowth        = ParseAvDecimal(root, "QuarterlyRevenueGrowthYOY");
            var revenueTTM       = ParseAvDecimal(root, "RevenueTTM");
            var grossMargins     = ParseAvDecimal(root, "GrossProfitTTM") is { } gp && revenueTTM is { } rev && rev != 0
                                       ? gp / rev
                                       : (decimal?)null;

            // Analyst consensus from the 5 rating-count fields
            var (analystConsensus, analystMean) = ParseAnalystRatings(root);

            // INCOME_STATEMENT → annual history
            var annualHistory = ParseAnnualHistory(incomeStatement);

            // CASH_FLOW → FCF (TTM from last 4 quarters)
            var freeCashflow = ParseCashFlow(cashFlow, symbol, _logger);

            // BALANCE_SHEET → total cash and total debt (most recent quarter)
            var (totalCash, totalDebt) = ParseBalanceSheet(balanceSheet);

            return new FinancialSnapshot
            {
                Ticker           = yahooSymbol,
                MarketCap        = marketCap,
                TrailingPE       = trailingPE,
                ForwardPE        = forwardPE,
                TrailingEps      = trailingEps,
                ForwardEps       = null,            // not available in AV OVERVIEW
                RevenueTTM       = revenueTTM,
                RevenueGrowthYoY = revGrowth,
                GrossMargins     = grossMargins,
                OperatingMargins = operatingMargins,
                ProfitMargins    = profitMargins,
                FreeCashflow     = freeCashflow,
                TotalCash        = totalCash,
                TotalDebt        = totalDebt,
                AnalystConsensus = analystConsensus,
                AnalystMean      = analystMean,
                NextEarningsDate = null,            // requires separate AV endpoint; omitted
                AnnualHistory    = annualHistory,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Alpha Vantage financial data for {Symbol}", symbol);
            return null;
        }
    }

    // ── HTTP helper ──────────────────────────────────────────────────────────

    private async Task<JsonDocument?> FetchJsonAsync(string queryString, CancellationToken ct)
    {
        var url = $"{_settings.BaseUrl.TrimEnd('/')}?{queryString}&apikey={Uri.EscapeDataString(_settings.ApiKey)}";

        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Alpha Vantage returned {Status} for query: {Query}", (int)response.StatusCode, queryString);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    // ── Parsing helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Alpha Vantage returns all values as JSON strings.
    /// "None" (and empty strings) are treated as null.
    /// </summary>
    private static decimal? ParseAvDecimal(JsonElement parent, string fieldName)
    {
        if (!parent.TryGetProperty(fieldName, out var field))
        {
            return null;
        }

        var raw = field.ValueKind == JsonValueKind.String
            ? field.GetString()
            : field.GetRawText();

        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
    }

    private static string? ParseAvString(JsonElement parent, string fieldName)
    {
        if (!parent.TryGetProperty(fieldName, out var field) ||
            field.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var s = field.GetString();
        return string.IsNullOrWhiteSpace(s) || s.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? null
            : s;
    }

    /// <summary>
    /// Derives analyst consensus key and mean from Alpha Vantage's five rating-count fields.
    /// AV provides: AnalystRatingStrongBuy, AnalystRatingBuy, AnalystRatingHold, AnalystRatingSell, AnalystRatingStrongSell
    /// We compute weighted mean (Strong Buy=1, Buy=2, Hold=3, Sell=4, Strong Sell=5) then map:
    ///   &lt;1.75 → "strong-buy", 1.75–2.5 → "buy", 2.5–3.5 → "hold", 3.5–4.5 → "sell", &gt;4.5 → "strong-sell"
    /// </summary>
    private static (string? consensus, decimal? mean) ParseAnalystRatings(JsonElement root)
    {
        var sb  = ParseAvDecimal(root, "AnalystRatingStrongBuy")  ?? 0;
        var b   = ParseAvDecimal(root, "AnalystRatingBuy")        ?? 0;
        var h   = ParseAvDecimal(root, "AnalystRatingHold")       ?? 0;
        var s   = ParseAvDecimal(root, "AnalystRatingSell")       ?? 0;
        var ss  = ParseAvDecimal(root, "AnalystRatingStrongSell") ?? 0;

        var total = sb + b + h + s + ss;
        if (total == 0)
        {
            return (null, null);
        }

        var mean = (sb * 1 + b * 2 + h * 3 + s * 4 + ss * 5) / total;

        var key = mean switch
        {
            < 1.75m => "strong-buy",
            < 2.5m  => "buy",
            < 3.5m  => "hold",
            < 4.5m  => "sell",
            _       => "strong-sell",
        };

        return (key, mean);
    }

    /// <summary>
    /// Parses annual income statement reports into the shared AnnualFinancials list.
    /// Reports are newest-first in AV; we reverse to oldest-first.
    /// </summary>
    private static IReadOnlyList<AnnualFinancials> ParseAnnualHistory(JsonDocument? doc)
    {
        if (doc is null ||
            !doc.RootElement.TryGetProperty("annualReports", out var reports) ||
            reports.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<AnnualFinancials>();

        foreach (var report in reports.EnumerateArray().Take(4))
        {
            // fiscalDateEnding is a plain date string: "2024-12-31"
            var dateStr = ParseAvString(report, "fiscalDateEnding");
            if (dateStr is null ||
                !DateOnly.TryParse(dateStr, CultureInfo.InvariantCulture, out var date))
            {
                continue;
            }

            var revenue   = ParseAvDecimal(report, "totalRevenue");
            var netIncome = ParseAvDecimal(report, "netIncome");

            results.Add(new AnnualFinancials(date.Year, revenue, netIncome));
        }

        // AV returns newest-first; reverse so the enricher/prompt reads oldest→newest trend.
        results.Sort((a, b) => a.Year.CompareTo(b.Year));
        return results;
    }

    /// <summary>
    /// Computes trailing-twelve-month (TTM) free cash flow from the last 4 quarterly reports.
    /// This gives the most recent data available (e.g. Q2-2025 through Q1-2026 for a March quarter-end).
    /// Falls back to the most recent annual report if quarterly data is insufficient.
    /// FCF per quarter = operatingCashflow − |capitalExpenditures| (when freeCashFlow is "None").
    /// </summary>
    private static decimal? ParseCashFlow(JsonDocument? doc, string symbol, ILogger logger)
    {
        if (doc is null)
        {
            logger.LogWarning("Alpha Vantage CASH_FLOW returned no data for {Symbol} — FCF will be absent", symbol);
            return null;
        }

        var root = doc.RootElement;

        // Prefer TTM from last 4 quarters — more recent than the annual figure.
        if (root.TryGetProperty("quarterlyReports", out var quarters) &&
            quarters.ValueKind == JsonValueKind.Array &&
            quarters.GetArrayLength() >= 4)
        {
            decimal ttmFcf    = 0;
            int validQuarters = 0;

            foreach (var q in quarters.EnumerateArray().Take(4))
            {
                var fcfQ = ParseAvDecimal(q, "freeCashFlow");
                if (fcfQ is null)
                {
                    var ocf   = ParseAvDecimal(q, "operatingCashflow");
                    var capex = ParseAvDecimal(q, "capitalExpenditures");
                    if (ocf.HasValue && capex.HasValue)
                    {
                        fcfQ = ocf.Value - Math.Abs(capex.Value);
                    }
                }

                if (fcfQ.HasValue)
                {
                    ttmFcf += fcfQ.Value;
                    validQuarters++;
                }
            }

            if (validQuarters == 4)
            {
                return ttmFcf;
            }

            if (validQuarters > 0)
            {
                // Scale partial quarters to a TTM estimate rather than return null.
                logger.LogWarning(
                    "Alpha Vantage CASH_FLOW: only {N}/4 quarters had FCF data for {Symbol} — scaling to TTM estimate",
                    validQuarters, symbol);
                return ttmFcf / validQuarters * 4;
            }
        }

        // Fallback: most recent annual report.
        if (root.TryGetProperty("annualReports", out var annuals) &&
            annuals.ValueKind == JsonValueKind.Array &&
            annuals.GetArrayLength() > 0)
        {
            var latest = annuals[0];
            var fcf    = ParseAvDecimal(latest, "freeCashFlow");
            if (fcf is null)
            {
                var ocf   = ParseAvDecimal(latest, "operatingCashflow");
                var capex = ParseAvDecimal(latest, "capitalExpenditures");
                if (ocf.HasValue && capex.HasValue)
                {
                    fcf = ocf.Value - Math.Abs(capex.Value);
                    logger.LogDebug("Alpha Vantage FCF for {Symbol}: computed from annual OCF - Capex", symbol);
                }
            }

            if (fcf is not null)
            {
                return fcf;
            }
        }

        logger.LogWarning("Alpha Vantage CASH_FLOW: could not compute FCF for {Symbol} — all relevant fields were None", symbol);
        return null;
    }

    /// <summary>
    /// Reads the most recent quarterly (or annual) balance sheet to get total cash and total debt.
    /// Cash = cashAndShortTermInvestments (preferred) or cashAndCashEquivalentsAtCarryingValue.
    /// Debt = shortLongTermDebtTotal.
    /// </summary>
    private static (decimal? totalCash, decimal? totalDebt) ParseBalanceSheet(JsonDocument? doc)
    {
        if (doc is null)
        {
            return (null, null);
        }

        var root = doc.RootElement;

        JsonElement report = default;
        bool found = false;

        if (root.TryGetProperty("quarterlyReports", out var quarters) &&
            quarters.ValueKind == JsonValueKind.Array &&
            quarters.GetArrayLength() > 0)
        {
            report = quarters[0];
            found  = true;
        }
        else if (root.TryGetProperty("annualReports", out var annuals) &&
                 annuals.ValueKind == JsonValueKind.Array &&
                 annuals.GetArrayLength() > 0)
        {
            report = annuals[0];
            found  = true;
        }

        if (!found)
        {
            return (null, null);
        }

        // Cash: prefer broader measure (cash + short-term investments), fall back to cash only.
        var totalCash = ParseAvDecimal(report, "cashAndShortTermInvestments")
                     ?? ParseAvDecimal(report, "cashAndCashEquivalentsAtCarryingValue");

        // Debt: sum of short-term + long-term debt obligations.
        var totalDebt = ParseAvDecimal(report, "shortLongTermDebtTotal");

        return (totalCash, totalDebt);
    }

    /// <summary>
    /// Strips Yahoo Finance symbol suffixes (e.g. ".L", "-B", ".TO") not used by Alpha Vantage.
    /// US tickers are passed through unchanged.
    /// </summary>
    private static string NormaliseSymbol(string yahooSymbol)
    {
        var dot = yahooSymbol.IndexOf('.');
        return dot > 0 ? yahooSymbol[..dot] : yahooSymbol;
    }
}
