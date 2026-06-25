using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MCP.HelloWorldData;

public sealed class MarketDataToolProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MarketDataToolProvider> _logger;

    public MarketDataToolProvider(IHttpClientFactory httpClientFactory, ILogger<MarketDataToolProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(EdgarCompanyFactsAsync, "edgar_company_facts", "Fetch SEC EDGAR financial data for a company by ticker symbol"),
            AIFunctionFactory.Create(StockQuoteAsync, "stock_quote", "Get current stock price and daily change from Yahoo Finance"),
            AIFunctionFactory.Create(StockHistoryAsync, "stock_history", "Get historical price data from Yahoo Finance"),
        ];
    }

    [Description("Fetch SEC EDGAR financial data for a company by ticker symbol")]
    private async Task<string> EdgarCompanyFactsAsync(
        [Description("Stock ticker symbol (e.g. AAPL, MSFT)")] string ticker,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Fetching EDGAR data for {Ticker}", ticker);

        try
        {
            ticker = ticker.ToUpperInvariant();
            var client = _httpClientFactory.CreateClient("SEC");

            var tickerMap = await client.GetFromJsonAsync<Dictionary<string, JsonElement>>(
                "https://www.sec.gov/files/company_tickers.json", ct) ?? [];

            string? cik = null;
            foreach (var entry in tickerMap.Values)
            {
                if (entry.GetProperty("ticker").GetString()?.Equals(ticker, StringComparison.OrdinalIgnoreCase) == true)
                {
                    cik = entry.GetProperty("cik_str").GetRawText().Trim('"');
                    break;
                }
            }

            if (cik is null)
                return JsonSerializer.Serialize(new { error = $"Ticker '{ticker}' not found in SEC EDGAR" });

            var paddedCik = cik.PadLeft(10, '0');
            var factsUrl = $"https://data.sec.gov/api/xbrl/companyfacts/CIK{paddedCik}.json";
            var factsJson = await client.GetStringAsync(factsUrl, ct);
            var facts = JsonSerializer.Deserialize<JsonElement>(factsJson);

            var entityName = facts.GetProperty("entityName").GetString();
            var usGaap = facts.TryGetProperty("facts", out var factsNode)
                         && factsNode.TryGetProperty("us-gaap", out var gaap)
                ? gaap
                : (JsonElement?)null;

            var summary = new Dictionary<string, object?> { ["ticker"] = ticker, ["entityName"] = entityName };

            string[] keyMetrics = ["Revenues", "NetIncomeLoss", "Assets", "Liabilities",
                                   "StockholdersEquity", "EarningsPerShareBasic",
                                   "OperatingIncomeLoss", "CashAndCashEquivalentsAtCarryingValue"];

            foreach (var metric in keyMetrics)
            {
                if (usGaap?.TryGetProperty(metric, out var metricNode) == true &&
                    metricNode.TryGetProperty("units", out var units))
                {
                    foreach (var unit in units.EnumerateObject())
                    {
                        var entries = unit.Value.EnumerateArray().ToArray();
                        var recent = entries
                            .Where(e => e.TryGetProperty("form", out var f) &&
                                        (f.GetString() == "10-K" || f.GetString() == "10-Q"))
                            .OrderByDescending(e => e.GetProperty("end").GetString())
                            .Take(4)
                            .Select(e => new
                            {
                                period = e.GetProperty("end").GetString(),
                                value = e.GetProperty("val").GetDouble(),
                                form = e.GetProperty("form").GetString(),
                            })
                            .ToArray();

                        if (recent.Length > 0)
                            summary[metric] = recent;
                        break;
                    }
                }
            }

            _logger.LogInformation("EDGAR data for {Ticker} fetched in {ElapsedMs}ms", ticker, sw.ElapsedMilliseconds);
            return JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "EDGAR fetch failed for {Ticker}", ticker);
            return JsonSerializer.Serialize(new { error = $"EDGAR fetch failed: {ex.Message}" });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("EDGAR fetch timed out for {Ticker}", ticker);
            return JsonSerializer.Serialize(new { error = "EDGAR fetch timed out" });
        }
    }

    [Description("Get current stock price and daily change from Yahoo Finance")]
    private async Task<string> StockQuoteAsync(
        [Description("Stock ticker symbol (e.g. AAPL, MSFT)")] string symbol,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Fetching Yahoo quote for {Symbol}", symbol);

        try
        {
            symbol = symbol.ToUpperInvariant();
            var client = _httpClientFactory.CreateClient("Yahoo");
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?range=1d&interval=1d";
            var json = await client.GetStringAsync(url, ct);
            var root = JsonSerializer.Deserialize<JsonElement>(json);

            var result = root.GetProperty("chart").GetProperty("result");
            if (result.GetArrayLength() == 0)
                return JsonSerializer.Serialize(new { error = $"No data found for '{symbol}'" });

            var item = result[0];
            var meta = item.GetProperty("meta");

            var quote = new
            {
                symbol,
                currency = meta.GetProperty("currency").GetString(),
                regularMarketPrice = meta.GetProperty("regularMarketPrice").GetDouble(),
                previousClose = meta.GetProperty("chartPreviousClose").GetDouble(),
                change = Math.Round(meta.GetProperty("regularMarketPrice").GetDouble() - meta.GetProperty("chartPreviousClose").GetDouble(), 4),
                changePercent = Math.Round(
                    (meta.GetProperty("regularMarketPrice").GetDouble() - meta.GetProperty("chartPreviousClose").GetDouble())
                    / meta.GetProperty("chartPreviousClose").GetDouble() * 100, 2),
                exchangeName = meta.TryGetProperty("exchangeName", out var ex) ? ex.GetString() : null,
                marketState = meta.TryGetProperty("currentTradingPeriod", out var tp) && tp.TryGetProperty("regular", out var reg)
                    ? "regular" : "unknown",
            };

            _logger.LogInformation("Yahoo quote for {Symbol} fetched in {ElapsedMs}ms", symbol, sw.ElapsedMilliseconds);
            return JsonSerializer.Serialize(quote, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Yahoo quote failed for {Symbol}", symbol);
            return JsonSerializer.Serialize(new { error = $"Yahoo quote failed: {ex.Message}" });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Yahoo quote timed out for {Symbol}", symbol);
            return JsonSerializer.Serialize(new { error = "Yahoo quote timed out" });
        }
    }

    [Description("Get historical price data from Yahoo Finance")]
    private async Task<string> StockHistoryAsync(
        [Description("Stock ticker symbol (e.g. AAPL, MSFT)")] string symbol,
        [Description("Time range: 5d, 1mo, 3mo, 6mo, 1y, 5y")] string range,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Fetching Yahoo history for {Symbol} range={Range}", symbol, range);

        var validRanges = new HashSet<string> { "5d", "1mo", "3mo", "6mo", "1y", "5y" };
        if (!validRanges.Contains(range))
            range = "1mo";

        var interval = range switch
        {
            "5d" => "1d",
            "1mo" => "1d",
            "3mo" => "1wk",
            "6mo" => "1wk",
            _ => "1mo",
        };

        try
        {
            symbol = symbol.ToUpperInvariant();
            var client = _httpClientFactory.CreateClient("Yahoo");
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?range={range}&interval={interval}";
            var json = await client.GetStringAsync(url, ct);
            var root = JsonSerializer.Deserialize<JsonElement>(json);

            var result = root.GetProperty("chart").GetProperty("result");
            if (result.GetArrayLength() == 0)
                return JsonSerializer.Serialize(new { error = $"No data found for '{symbol}'" });

            var item = result[0];
            var timestamps = item.GetProperty("timestamp").EnumerateArray().Select(t => t.GetInt64()).ToArray();
            var indicators = item.GetProperty("indicators").GetProperty("quote")[0];
            var closes = indicators.GetProperty("close").EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.Null ? (double?)null : v.GetDouble()).ToArray();
            var volumes = indicators.GetProperty("volume").EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.Null ? (long?)null : v.GetInt64()).ToArray();

            var dataPoints = new List<object>();
            for (var i = 0; i < timestamps.Length && i < closes.Length; i++)
            {
                if (closes[i] is null) continue;
                dataPoints.Add(new
                {
                    date = DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).ToString("yyyy-MM-dd"),
                    close = Math.Round(closes[i]!.Value, 2),
                    volume = i < volumes.Length ? volumes[i] : null,
                });
            }

            var history = new
            {
                symbol,
                range,
                interval,
                dataPoints = dataPoints.TakeLast(50),
            };

            _logger.LogInformation("Yahoo history for {Symbol} fetched in {ElapsedMs}ms ({Count} points)", symbol, sw.ElapsedMilliseconds, dataPoints.Count);
            return JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Yahoo history failed for {Symbol}", symbol);
            return JsonSerializer.Serialize(new { error = $"Yahoo history failed: {ex.Message}" });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Yahoo history timed out for {Symbol}", symbol);
            return JsonSerializer.Serialize(new { error = "Yahoo history timed out" });
        }
    }
}
