using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Detects holdings embedded in a markdown portfolio prompt and prepends a live market snapshot
/// plus recent macro news headlines.
/// </summary>
public class FinancePromptEnricher : IFinancePromptEnricher
{
    private readonly IFreeMarketDataService _marketDataService;
    private readonly IMarketNewsService _newsService;
    private readonly IInsiderDataService _insiderDataService;
    private readonly IFinancialDataService _financialDataService;
    private readonly ILogger<FinancePromptEnricher> _logger;

    public FinancePromptEnricher()
        : this(new EmptyMarketDataService(), new EmptyNewsService(),
               new EmptyInsiderDataService(), new EmptyFinancialDataService(),
               NullLogger<FinancePromptEnricher>.Instance)
    {
    }

    // Kept for backward compatibility (used by existing tests).
    public FinancePromptEnricher(
        IFreeMarketDataService marketDataService,
        ILogger<FinancePromptEnricher> logger)
        : this(marketDataService, new EmptyNewsService(),
               new EmptyInsiderDataService(), new EmptyFinancialDataService(), logger)
    {
    }

    public FinancePromptEnricher(
        IFreeMarketDataService marketDataService,
        IMarketNewsService newsService,
        ILogger<FinancePromptEnricher> logger)
        : this(marketDataService, newsService,
               new EmptyInsiderDataService(), new EmptyFinancialDataService(), logger)
    {
    }

    public FinancePromptEnricher(
        IFreeMarketDataService marketDataService,
        IMarketNewsService newsService,
        IInsiderDataService insiderDataService,
        ILogger<FinancePromptEnricher> logger)
        : this(marketDataService, newsService, insiderDataService,
               new EmptyFinancialDataService(), logger)
    {
    }

    public FinancePromptEnricher(
        IFreeMarketDataService marketDataService,
        IMarketNewsService newsService,
        IInsiderDataService insiderDataService,
        IFinancialDataService financialDataService,
        ILogger<FinancePromptEnricher> logger)
    {
        _marketDataService    = marketDataService;
        _newsService          = newsService;
        _insiderDataService   = insiderDataService;
        _financialDataService = financialDataService;
        _logger               = logger;
    }

    public async Task<Topic> EnrichTopicAsync(Topic topic, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);

        if (string.IsNullOrWhiteSpace(topic.Prompt))
        {
            return topic;
        }

        var holdings = ParseHoldings(topic.Prompt);
        if (holdings.Count == 0)
        {
            return topic;
        }

        IReadOnlyList<MarketSnapshot> snapshots;
        try
        {
            snapshots = await _marketDataService.GetSnapshotsAsync(holdings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich finance prompt for topic {Topic}", topic.Name);
            return topic;
        }

        IReadOnlyList<NewsItem> newsItems;
        try
        {
            newsItems = await _newsService.GetMacroNewsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch macro news for topic {Topic}", topic.Name);
            newsItems = [];
        }

        // Fetch company-specific headlines for each direct-stock holding.
        // This covers arbitrary deep-dive tickers that are not in the hardcoded portfolio RSS list.
        var stockNews = new Dictionary<string, IReadOnlyList<NewsItem>>(StringComparer.OrdinalIgnoreCase);
        var snapshotByTicker = snapshots.ToDictionary(s => s.Ticker, StringComparer.OrdinalIgnoreCase);

        foreach (var holding in holdings)
        {
            if (!IsDirectStock(holding.Type))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var yahooSymbol = snapshotByTicker.TryGetValue(holding.Ticker, out var snapN) &&
                              !string.IsNullOrWhiteSpace(snapN.ProviderSymbol)
                ? snapN.ProviderSymbol
                : holding.Ticker;

            try
            {
                var items = await _newsService.GetStockNewsAsync(yahooSymbol, 5, cancellationToken);
                if (items.Count > 0)
                {
                    stockNews[holding.Ticker] = items;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch stock news for {Ticker}", holding.Ticker);
            }
        }

        // Fetch insider trades for direct stock holdings only (skip ETFs, Crypto, Commodities).
        var insiderTrades = new Dictionary<string, IReadOnlyList<InsiderTrade>>(StringComparer.OrdinalIgnoreCase);

        foreach (var holding in holdings)
        {
            if (!IsDirectStock(holding.Type))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Prefer the provider symbol (e.g. "MSFT" for display ticker "MSF") when it has
            // no exchange suffix (no dot); otherwise fall back to the display ticker.
            var edgarTicker = holding.Ticker;
            if (snapshotByTicker.TryGetValue(holding.Ticker, out var snap) &&
                !string.IsNullOrWhiteSpace(snap.ProviderSymbol) &&
                !snap.ProviderSymbol.Contains('.'))
            {
                edgarTicker = snap.ProviderSymbol;
            }

            try
            {
                var trades = await _insiderDataService.GetRecentTradesAsync(edgarTicker, cancellationToken);
                if (trades.Count > 0)
                {
                    insiderTrades[holding.Ticker] = trades;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch insider data for {Ticker}", holding.Ticker);
            }
        }

        // Fetch financial fundamentals — only when the topic is a focused single-stock deep-dive
        // (holdings.Count <= MaxHoldingsForEnrichment) to avoid calling Yahoo for 29-stock portfolios.
        var financialSnapshots = new Dictionary<string, FinancialSnapshot>(StringComparer.OrdinalIgnoreCase);

        if (_financialDataService is not EmptyFinancialDataService)
        {
            foreach (var holding in holdings)
            {
                if (!IsDirectStock(holding.Type))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                var yahooSymbol = snapshotByTicker.TryGetValue(holding.Ticker, out var snap2) &&
                                  !string.IsNullOrWhiteSpace(snap2.ProviderSymbol)
                    ? snap2.ProviderSymbol
                    : holding.Ticker;

                try
                {
                    var fs = await _financialDataService.GetFinancialSnapshotAsync(yahooSymbol, cancellationToken);
                    if (fs is not null)
                    {
                        financialSnapshots[holding.Ticker] = fs;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch financial data for {Ticker}", holding.Ticker);
                }
            }
        }

        if (snapshots.Count == 0 && newsItems.Count == 0 && stockNews.Count == 0 && insiderTrades.Count == 0 && financialSnapshots.Count == 0)
        {
            return topic;
        }

        var prefix = new StringBuilder();
        if (snapshots.Count > 0)
        {
            prefix.AppendLine(BuildMarketDataSection(snapshots));
        }
        if (newsItems.Count > 0)
        {
            prefix.AppendLine();
            prefix.AppendLine(BuildNewsSection(newsItems));
        }
        if (stockNews.Count > 0)
        {
            prefix.AppendLine();
            prefix.AppendLine(BuildStockNewsSection(stockNews));
        }
        if (insiderTrades.Count > 0)
        {
            prefix.AppendLine();
            prefix.AppendLine(BuildInsiderSection(insiderTrades));
        }
        if (financialSnapshots.Count > 0)
        {
            prefix.AppendLine();
            prefix.AppendLine(BuildFinancialSection(financialSnapshots));
        }

        return new Topic
        {
            Name = topic.Name,
            Description = topic.Description,
            Prompt = $"{prefix.ToString().TrimEnd()}\n\n{topic.Prompt}",
        };
    }

    private static IReadOnlyList<PortfolioHolding> ParseHoldings(string prompt)
    {
        var lines = prompt.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var holdings = new List<PortfolioHolding>();
        var inTable = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (!inTable)
            {
                if (line.StartsWith("| Ticker", StringComparison.OrdinalIgnoreCase))
                {
                    inTable = true;
                }

                continue;
            }

            if (!line.StartsWith('|'))
            {
                break;
            }

            if (IsSeparatorRow(line))
            {
                continue;
            }

            var cells = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length < 5)
            {
                continue;
            }

            holdings.Add(new PortfolioHolding
            {
                Ticker = cells[0],
                Name = cells[1],
                Type = cells[2],
                Quantity = ParseDecimal(cells[3]),
                AverageBuyPrice = ParseDecimal(cells[4]),
            });
        }

        return holdings;
    }

    private static string BuildMarketDataSection(IReadOnlyList<MarketSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Live Market Data Snapshot");
        builder.AppendLine($"Generated at: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine("Source currencies are shown as returned by each upstream provider.");
        builder.AppendLine();

        foreach (var snapshot in snapshots)
        {
            builder.Append("- ");
            builder.Append(snapshot.Ticker);

            if (!string.IsNullOrWhiteSpace(snapshot.ProviderSymbol) &&
                !string.Equals(snapshot.ProviderSymbol, snapshot.Ticker, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" (");
                builder.Append(snapshot.ProviderSymbol);
                builder.Append(')');
            }

            builder.Append(": ");

            if (!snapshot.IsAvailable || snapshot.CurrentPrice is null)
            {
                builder.Append("live data unavailable");
                if (!string.IsNullOrWhiteSpace(snapshot.Note))
                {
                    builder.Append(" (");
                    builder.Append(snapshot.Note);
                    builder.Append(')');
                }

                builder.AppendLine(".");
                continue;
            }

            builder.Append(FormatDecimal(snapshot.CurrentPrice.Value));
            if (!string.IsNullOrWhiteSpace(snapshot.Currency))
            {
                builder.Append(' ');
                builder.Append(snapshot.Currency);
            }

            if (snapshot.DailyChangePercent is not null)
            {
                builder.Append(", 1d ");
                builder.Append(FormatPercent(snapshot.DailyChangePercent.Value));
            }

            if (snapshot.WeeklyChangePercent is not null)
            {
                builder.Append(IsCrypto(snapshot) ? ", 7d " : ", 5d ");
                builder.Append(FormatPercent(snapshot.WeeklyChangePercent.Value));
            }

            if (snapshot.AsOf is not null)
            {
                builder.Append(", as of ");
                builder.Append(snapshot.AsOf.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsSeparatorRow(string line)
    {
        foreach (var ch in line)
        {
            if (ch is not ('|' or '-' or ':' or ' '))
            {
                return false;
            }
        }

        return true;
    }

    private static decimal? ParseDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(decimal value)
    {
        var sign = value > 0 ? "+" : string.Empty;
        return $"{sign}{value.ToString("0.##", CultureInfo.InvariantCulture)}%";
    }

    private static bool IsCrypto(MarketSnapshot snapshot) =>
        string.Equals(snapshot.AssetType, "Crypto", StringComparison.OrdinalIgnoreCase);

    private static string BuildNewsSection(IReadOnlyList<NewsItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Recent Macro & Market News");
        builder.AppendLine($"Retrieved: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine();

        foreach (var item in items)
        {
            var timestamp = item.PublishedAt.HasValue
                ? item.PublishedAt.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
                : "date unknown";
            var source = string.IsNullOrWhiteSpace(item.Source) ? "Yahoo Finance" : item.Source;

            builder.Append($"- [{timestamp} | {source}] ");
            builder.AppendLine(item.Title);

            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                builder.AppendLine($"  {item.Description}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildStockNewsSection(IReadOnlyDictionary<string, IReadOnlyList<NewsItem>> newsByTicker)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Company-Specific Headlines");
        builder.AppendLine($"Retrieved: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine("Source: Yahoo Finance RSS (per-stock feeds). These are direct company news — distinct from the macro headlines above.");
        builder.AppendLine();

        foreach (var (ticker, items) in newsByTicker)
        {
            builder.AppendLine($"### {ticker}");

            foreach (var item in items)
            {
                var timestamp = item.PublishedAt.HasValue
                    ? item.PublishedAt.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
                    : "date unknown";

                builder.Append($"- [{timestamp}] ");
                builder.AppendLine(item.Title);

                if (!string.IsNullOrWhiteSpace(item.Description))
                {
                    builder.AppendLine($"  {item.Description}");
                }
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private sealed class EmptyMarketDataService : IFreeMarketDataService
    {
        public Task<IReadOnlyList<MarketSnapshot>> GetSnapshotsAsync(
            IReadOnlyList<PortfolioHolding> holdings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MarketSnapshot>>([]);
        }
    }

    private sealed class EmptyNewsService : IMarketNewsService
    {
        public Task<IReadOnlyList<NewsItem>> GetMacroNewsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NewsItem>>([]);

        public Task<IReadOnlyList<NewsItem>> GetStockNewsAsync(
            string yahooSymbol, int maxItems, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NewsItem>>([]);
    }

    private sealed class EmptyInsiderDataService : IInsiderDataService
    {
        public Task<IReadOnlyList<InsiderTrade>> GetRecentTradesAsync(
            string ticker,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InsiderTrade>>([]);
    }

    private sealed class EmptyFinancialDataService : IFinancialDataService
    {
        public Task<FinancialSnapshot?> GetFinancialSnapshotAsync(
            string yahooSymbol,
            CancellationToken cancellationToken = default)
            => Task.FromResult<FinancialSnapshot?>(null);
    }

    private static bool IsDirectStock(string assetType) =>
        string.Equals(assetType, "Stock", StringComparison.OrdinalIgnoreCase);

    private static string BuildInsiderSection(
        IReadOnlyDictionary<string, IReadOnlyList<InsiderTrade>> tradesByTicker)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Recent Insider Activity");
        sb.AppendLine($"Retrieved: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine("Source: SEC EDGAR Form 4 filings (open-market and derivative transactions).");
        sb.AppendLine();

        foreach (var (ticker, trades) in tradesByTicker)
        {
            sb.AppendLine($"### {ticker}");

            foreach (var t in trades)
            {
                string direction;
                if (t.Kind == InsiderTransactionKind.Derivative)
                {
                    direction = t.AcquiredOrDisposed switch
                    {
                        "A" => "RSU VEST / OPTION ACQUIRE",
                        "D" => "RSU DISPOSE / OPTION EXERCISE",
                        _   => $"DERIVATIVE ({t.AcquiredOrDisposed})",
                    };
                }
                else
                {
                    direction = t.AcquiredOrDisposed switch
                    {
                        "A" => "OPEN-MARKET BUY",
                        "D" => "OPEN-MARKET SELL",
                        _   => t.AcquiredOrDisposed,
                    };
                }

                var date = t.TransactionDate.HasValue
                    ? t.TransactionDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : "date unknown";

                var shares = t.Shares.HasValue
                    ? t.Shares.Value.ToString("N0", CultureInfo.InvariantCulture) + " shares"
                    : "unknown shares";

                var price = t.PricePerShare.HasValue
                    ? "@ $" + t.PricePerShare.Value.ToString("0.##", CultureInfo.InvariantCulture)
                    : string.Empty;

                var total = (t.Shares.HasValue && t.PricePerShare.HasValue)
                    ? $" = ${t.Shares.Value * t.PricePerShare.Value:N0}"
                    : string.Empty;

                var title = string.IsNullOrWhiteSpace(t.InsiderTitle) ? "" : $" ({t.InsiderTitle})";

                sb.AppendLine($"- {date}: {t.InsiderName}{title} — **{direction}** {shares} {price}{total}");
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildFinancialSection(
        IReadOnlyDictionary<string, FinancialSnapshot> snapshotsByTicker)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Financial Fundamentals");
        sb.AppendLine($"Retrieved: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine("Source: Yahoo Finance quoteSummary API (live data).");
        sb.AppendLine();

        foreach (var (ticker, fs) in snapshotsByTicker)
        {
            sb.AppendLine($"### {ticker}");

            if (fs.MarketCap.HasValue)
            {
                sb.AppendLine($"- **Market Cap (live)**: {FormatLargeNumber(fs.MarketCap.Value)}");
            }
            if (fs.TrailingPE.HasValue)
            {
                sb.AppendLine($"- **Trailing P/E**: {fs.TrailingPE.Value:0.##}x");
            }
            if (fs.ForwardPE.HasValue)
            {
                sb.AppendLine($"- **Forward P/E**: {fs.ForwardPE.Value:0.##}x");
            }
            if (fs.TrailingEps.HasValue)
            {
                sb.AppendLine($"- **Trailing EPS**: ${fs.TrailingEps.Value:0.##}");
            }
            if (fs.ForwardEps.HasValue)
            {
                sb.AppendLine($"- **Forward EPS**: ${fs.ForwardEps.Value:0.##}");
            }
            if (fs.RevenueTTM.HasValue)
            {
                sb.AppendLine($"- **Revenue (TTM)**: {FormatLargeNumber(fs.RevenueTTM.Value)}");
            }
            if (fs.RevenueGrowthYoY.HasValue)
            {
                sb.AppendLine($"- **Revenue Growth YoY**: {fs.RevenueGrowthYoY.Value:P1}");
            }
            if (fs.GrossMargins.HasValue)
            {
                sb.AppendLine($"- **Gross Margin**: {fs.GrossMargins.Value:P1}");
            }
            if (fs.OperatingMargins.HasValue)
            {
                sb.AppendLine($"- **Operating Margin**: {fs.OperatingMargins.Value:P1}");
            }
            if (fs.ProfitMargins.HasValue)
            {
                sb.AppendLine($"- **Net Profit Margin**: {fs.ProfitMargins.Value:P1}");
            }
            if (fs.FreeCashflow.HasValue)
            {
                sb.AppendLine($"- **Free Cash Flow**: {FormatLargeNumber(fs.FreeCashflow.Value)}");
            }
            if (fs.TotalCash.HasValue)
            {
                sb.AppendLine($"- **Total Cash**: {FormatLargeNumber(fs.TotalCash.Value)}");
            }
            if (fs.TotalDebt.HasValue)
            {
                sb.AppendLine($"- **Total Debt**: {FormatLargeNumber(fs.TotalDebt.Value)}");
            }

            if (!string.IsNullOrWhiteSpace(fs.AnalystConsensus))
            {
                var mean = fs.AnalystMean.HasValue
                    ? $" (mean {fs.AnalystMean.Value:0.0} / 5.0 — 1.0 = strong buy, 5.0 = sell)"
                    : string.Empty;
                sb.AppendLine($"- **Analyst Consensus**: {fs.AnalystConsensus}{mean}");
            }

            if (fs.NextEarningsDate.HasValue)
            {
                sb.AppendLine($"- **Next Earnings Date**: {fs.NextEarningsDate.Value:yyyy-MM-dd} (source: Yahoo Finance)");
            }

            if (fs.AnnualHistory.Count > 0)
            {
                sb.AppendLine("- **Annual Revenue / Net Income**:");
                foreach (var year in fs.AnnualHistory)
                {
                    var rev = year.Revenue.HasValue ? FormatLargeNumber(year.Revenue.Value) : "n/a";
                    var ni  = year.NetIncome.HasValue ? FormatLargeNumber(year.NetIncome.Value) : "n/a";
                    sb.AppendLine($"  - {year.Year}: Revenue {rev}, Net Income {ni}");
                }
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatLargeNumber(decimal value)
    {
        var abs = Math.Abs(value);
        var sign = value < 0 ? "-" : string.Empty;
        return abs switch
        {
            >= 1_000_000_000_000m => $"{sign}${abs / 1_000_000_000_000m:0.##}T",
            >= 1_000_000_000m    => $"{sign}${abs / 1_000_000_000m:0.##}B",
            >= 1_000_000m        => $"{sign}${abs / 1_000_000m:0.##}M",
            _                    => $"{sign}${abs:N0}",
        };
    }
}