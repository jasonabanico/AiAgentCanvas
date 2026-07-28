using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataConnection.VectorSearch.Snowflake;

/// <summary>
/// Exposes Snowflake Cortex Search as an agent tool (<c>snowflake_cortex_search</c>) for RAG
/// grounding against a Cortex Search service. Uses the Cortex Search query REST API. The local
/// SQLite vector store stays the default RAG store; this tool is additive, so the agent can
/// ground answers in Snowflake-hosted data on demand.
/// </summary>
public sealed class SnowflakeCortexSearchToolProvider
{
    private static readonly string[] DefaultColumns = ["chunk"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SnowflakeCortexSearchOptions _options;
    private readonly ILogger<SnowflakeCortexSearchToolProvider> _logger;

    public SnowflakeCortexSearchToolProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<SnowflakeCortexSearchOptions> options,
        ILogger<SnowflakeCortexSearchToolProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(QueryAsync, "snowflake_cortex_search",
                "Semantically search a Snowflake Cortex Search service for passages relevant to a query, for grounding answers in Snowflake-hosted data."),
        ];
    }

    [Description("Semantically search a Snowflake Cortex Search service and return the most relevant rows.")]
    private async Task<string> QueryAsync(
        [Description("Natural-language query to search for")] string query,
        [Description("Maximum number of results to return (optional)")] int? limit,
        CancellationToken ct)
    {
        if (!_options.IsConfigured)
            return JsonSerializer.Serialize(new { error = "Snowflake Cortex Search is not configured. Set Snowflake:CortexSearch AccountUrl, PersonalAccessToken, Database, Schema, and ServiceName in appsettings.json." });

        var sw = Stopwatch.StartNew();
        var columns = _options.Columns is { Length: > 0 } ? _options.Columns : DefaultColumns;
        var count = limit is > 0 ? limit.Value : _options.Limit;

        _logger.LogDebug("Cortex Search query on {Service} (limit={Count})", _options.ServiceName, count);

        try
        {
            var client = _httpClientFactory.CreateClient("SnowflakeCortexSearch");
            client.BaseAddress ??= new Uri(_options.AccountUrl!.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.PersonalAccessToken);

            var payload = new { query, columns, limit = count };

            using var response = await client.PostAsJsonAsync(_options.BuildQueryPath(), payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Cortex Search returned {Status}: {Body}", (int)response.StatusCode, body);
                return JsonSerializer.Serialize(new { error = $"Cortex Search query failed ({(int)response.StatusCode})", detail = Truncate(body, 500) });
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var rows = ParseRows(root);

            _logger.LogInformation("Cortex Search on {Service} returned {Count} rows in {ElapsedMs}ms",
                _options.ServiceName, rows.Count, sw.ElapsedMilliseconds);

            return JsonSerializer.Serialize(new { service = _options.ServiceName, query, results = rows },
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Cortex Search request failed");
            return JsonSerializer.Serialize(new { error = $"Cortex Search request failed: {ex.Message}" });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Cortex Search timed out");
            return JsonSerializer.Serialize(new { error = "Cortex Search request timed out" });
        }
    }

    /// <summary>
    /// The Cortex Search query response returns a <c>results</c> array whose elements are already
    /// column→value objects, so each is mapped straight to a dictionary.
    /// </summary>
    private static List<Dictionary<string, object?>> ParseRows(JsonElement root)
    {
        var rows = new List<Dictionary<string, object?>>();

        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var record = new Dictionary<string, object?>();
            foreach (var prop in item.EnumerateObject())
                record[prop.Name] = ToClrValue(prop.Value);
            rows.Add(record);
        }

        return rows;
    }

    private static object? ToClrValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => e.GetRawText(),
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
