using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCP.MarketData;

public sealed class MarketDataToolProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _alphaVantageKey;
    private readonly ILogger<MarketDataToolProvider> _logger;

    public MarketDataToolProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<MarketDataToolProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _alphaVantageKey = configuration["AlphaVantage:ApiKey"] ?? "demo";
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(EdgarCompanyFactsAsync, "edgar_company_facts", "Fetch SEC EDGAR financial data for a company by ticker symbol"),
            AIFunctionFactory.Create(StockQuoteAsync, "stock_quote", "Get current stock price and daily change from Alpha Vantage"),
            AIFunctionFactory.Create(StockTechnicalsAsync, "stock_technicals", "Get technical indicators (RSI, SMA, EMA, MACD) from Alpha Vantage"),
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

    [Description("Get current stock price and daily change from Alpha Vantage")]
    private async Task<string> StockQuoteAsync(
        [Description("Stock ticker symbol")] string symbol,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Fetching stock quote for {Symbol}", symbol);

        try
        {
            symbol = symbol.ToUpperInvariant();
            var client = _httpClientFactory.CreateClient("AlphaVantage");
            var url = $"https://www.alphavantage.co/query?function=GLOBAL_QUOTE&symbol={symbol}&apikey={_alphaVantageKey}";
            var result = await client.GetStringAsync(url, ct);

            _logger.LogInformation("Stock quote for {Symbol} fetched in {ElapsedMs}ms", symbol, sw.ElapsedMilliseconds);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Stock quote failed for {Symbol}", symbol);
            return JsonSerializer.Serialize(new { error = $"Stock quote failed: {ex.Message}" });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Stock quote timed out for {Symbol}", symbol);
            return JsonSerializer.Serialize(new { error = "Stock quote timed out" });
        }
    }

    [Description("Get technical indicators (RSI, SMA, EMA, MACD) from Alpha Vantage")]
    private async Task<string> StockTechnicalsAsync(
        [Description("Stock ticker symbol")] string symbol,
        [Description("Indicator name: RSI, SMA, EMA, MACD")] string indicator,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Fetching {Indicator} for {Symbol}", indicator, symbol);

        try
        {
            symbol = symbol.ToUpperInvariant();
            indicator = indicator.ToUpperInvariant();

            var client = _httpClientFactory.CreateClient("AlphaVantage");

            var queryParams = indicator switch
            {
                "RSI" => $"function=RSI&symbol={symbol}&interval=daily&time_period=14&series_type=close",
                "SMA" => $"function=SMA&symbol={symbol}&interval=daily&time_period=50&series_type=close",
                "EMA" => $"function=EMA&symbol={symbol}&interval=daily&time_period=20&series_type=close",
                "MACD" => $"function=MACD&symbol={symbol}&interval=daily&series_type=close",
                _ => $"function=RSI&symbol={symbol}&interval=daily&time_period=14&series_type=close",
            };

            var url = $"https://www.alphavantage.co/query?{queryParams}&apikey={_alphaVantageKey}";
            var response = await client.GetStringAsync(url, ct);

            var json = JsonSerializer.Deserialize<JsonElement>(response);
            var trimmed = TrimToRecentEntries(json, 10);

            _logger.LogInformation("{Indicator} for {Symbol} fetched in {ElapsedMs}ms", indicator, symbol, sw.ElapsedMilliseconds);
            return JsonSerializer.Serialize(trimmed, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Technicals fetch failed for {Symbol}", symbol);
            return JsonSerializer.Serialize(new { error = $"Technicals fetch failed: {ex.Message}" });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Technicals fetch timed out for {Symbol}", symbol);
            return JsonSerializer.Serialize(new { error = "Technicals fetch timed out" });
        }
    }

    private static JsonElement TrimToRecentEntries(JsonElement root, int count)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name.StartsWith("Technical Analysis") || prop.Name.StartsWith("Meta"))
            {
                if (prop.Name.StartsWith("Technical Analysis"))
                {
                    var entries = prop.Value.EnumerateObject()
                        .Take(count)
                        .ToDictionary(e => e.Name, e => (object)e.Value.ToString()!);
                    dict[prop.Name] = entries;
                }
                else
                {
                    dict[prop.Name] = prop.Value.ToString()!;
                }
            }
        }
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(dict));
    }
}
