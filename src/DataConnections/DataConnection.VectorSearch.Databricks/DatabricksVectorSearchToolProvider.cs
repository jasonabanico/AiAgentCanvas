using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataConnection.VectorSearch.Databricks;

/// <summary>
/// Exposes Databricks Vector Search as an agent tool (<c>databricks_vector_search</c>) for RAG
/// grounding against a Databricks-hosted index. Uses the Vector Search REST API with managed
/// embeddings (<c>query_text</c>). The local SQLite vector store stays the default RAG store;
/// this tool is additive, so the agent can ground answers in Databricks data on demand.
/// </summary>
public sealed class DatabricksVectorSearchToolProvider
{
    private static readonly string[] DefaultColumns = ["id", "text"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DatabricksVectorSearchOptions _options;
    private readonly ILogger<DatabricksVectorSearchToolProvider> _logger;

    public DatabricksVectorSearchToolProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<DatabricksVectorSearchOptions> options,
        ILogger<DatabricksVectorSearchToolProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(QueryAsync, "databricks_vector_search",
                "Semantically search a Databricks Vector Search index for passages relevant to a query, for grounding answers in Databricks-hosted data."),
        ];
    }

    [Description("Semantically search a Databricks Vector Search index and return the most relevant rows.")]
    private async Task<string> QueryAsync(
        [Description("Natural-language query to search for")] string query,
        [Description("Maximum number of results to return (optional)")] int? numResults,
        CancellationToken ct)
    {
        if (!_options.IsConfigured)
            return JsonSerializer.Serialize(new { error = "Databricks Vector Search is not configured. Set DatabricksVectorSearch:WorkspaceUrl, PersonalAccessToken, and IndexName in appsettings.json." });

        var sw = Stopwatch.StartNew();
        var columns = _options.Columns is { Length: > 0 } ? _options.Columns : DefaultColumns;
        var count = numResults is > 0 ? numResults.Value : _options.NumResults;

        _logger.LogDebug("Databricks Vector Search query on {Index} (numResults={Count})", _options.IndexName, count);

        try
        {
            var client = _httpClientFactory.CreateClient("DatabricksVectorSearch");
            client.BaseAddress ??= new Uri(_options.WorkspaceUrl!.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.PersonalAccessToken);

            var requestUri = $"api/2.0/vector-search/indexes/{_options.IndexName}/query";
            var payload = new { columns, query_text = query, num_results = count };

            using var response = await client.PostAsJsonAsync(requestUri, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Databricks Vector Search returned {Status}: {Body}", (int)response.StatusCode, body);
                return JsonSerializer.Serialize(new { error = $"Vector Search query failed ({(int)response.StatusCode})", detail = Truncate(body, 500) });
            }

            var root = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var rows = ParseRows(root);

            _logger.LogInformation("Databricks Vector Search on {Index} returned {Count} rows in {ElapsedMs}ms",
                _options.IndexName, rows.Count, sw.ElapsedMilliseconds);

            return JsonSerializer.Serialize(new { index = _options.IndexName, query, results = rows },
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Databricks Vector Search request failed");
            return JsonSerializer.Serialize(new { error = $"Vector Search request failed: {ex.Message}" });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Databricks Vector Search timed out");
            return JsonSerializer.Serialize(new { error = "Vector Search request timed out" });
        }
    }

    /// <summary>
    /// Maps the response's <c>manifest.columns[].name</c> onto each <c>result.data_array</c> row,
    /// producing a list of column→value objects.
    /// </summary>
    private static List<Dictionary<string, object?>> ParseRows(JsonElement root)
    {
        var rows = new List<Dictionary<string, object?>>();

        string[] columnNames = root.TryGetProperty("manifest", out var manifest)
            && manifest.TryGetProperty("columns", out var cols) && cols.ValueKind == JsonValueKind.Array
            ? cols.EnumerateArray()
                .Select(c => c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                .ToArray()
            : [];

        if (!root.TryGetProperty("result", out var result)
            || !result.TryGetProperty("data_array", out var dataArray)
            || dataArray.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var row in dataArray.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array) continue;
            var values = row.EnumerateArray().ToArray();
            var record = new Dictionary<string, object?>();
            for (var i = 0; i < values.Length; i++)
            {
                var key = i < columnNames.Length && !string.IsNullOrEmpty(columnNames[i]) ? columnNames[i] : $"col{i}";
                record[key] = ToClrValue(values[i]);
            }
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
