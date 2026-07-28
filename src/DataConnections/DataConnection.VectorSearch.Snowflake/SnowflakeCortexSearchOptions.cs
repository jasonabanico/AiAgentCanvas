namespace DataConnection.VectorSearch.Snowflake;

/// <summary>
/// Configuration for the Snowflake Cortex Search tool. Self-contained (its own account URL
/// and token) so it can be used for RAG grounding independent of which chat provider is active.
/// The local SQLite vector store remains the default IVectorStore; this tool is additive.
/// </summary>
public sealed class SnowflakeCortexSearchOptions
{
    public const string SectionName = "Snowflake:CortexSearch";

    /// <summary>Base account URL, e.g. <c>https://myorg-myaccount.snowflakecomputing.com</c>.</summary>
    public string? AccountUrl { get; set; }

    /// <summary>
    /// Snowflake Programmatic Access Token, sent as <c>Authorization: Bearer &lt;token&gt;</c>.
    /// Supply the token exactly as the account expects it (some accounts require a scheme prefix
    /// such as <c>pat/</c>; if so, include it here).
    /// </summary>
    public string? PersonalAccessToken { get; set; }

    /// <summary>Database containing the Cortex Search service, e.g. <c>MY_DB</c>.</summary>
    public string? Database { get; set; }

    /// <summary>Schema containing the Cortex Search service, e.g. <c>PUBLIC</c>.</summary>
    public string? Schema { get; set; }

    /// <summary>Cortex Search service name, e.g. <c>DOCS_SEARCH_SERVICE</c>.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Columns to return from each match, e.g. <c>["title", "content"]</c>. Defaults to <c>["chunk"]</c>.</summary>
    public string[]? Columns { get; set; }

    /// <summary>Default number of results to return when the tool caller does not specify one.</summary>
    public int Limit { get; set; } = 5;

    /// <summary>True when the minimum required settings are present to enable the tool.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountUrl)
        && !AccountUrl.Contains("YOUR-ACCOUNT")
        && !string.IsNullOrWhiteSpace(PersonalAccessToken)
        && !string.IsNullOrWhiteSpace(Database)
        && !string.IsNullOrWhiteSpace(Schema)
        && !string.IsNullOrWhiteSpace(ServiceName);

    /// <summary>Builds the query endpoint path (relative to the account base URL).</summary>
    public string BuildQueryPath() =>
        $"api/v2/databases/{Database}/schemas/{Schema}/cortex-search-services/{ServiceName}:query";
}
