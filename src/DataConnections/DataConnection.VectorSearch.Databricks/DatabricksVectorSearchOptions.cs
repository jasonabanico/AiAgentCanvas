namespace DataConnection.VectorSearch.Databricks;

/// <summary>
/// Configuration for the Databricks Vector Search tool. Self-contained (its own workspace URL
/// and token) so it can be used for RAG grounding independent of which chat provider is active.
/// The local SQLite vector store remains the default IVectorStore; this tool is additive.
/// </summary>
public sealed class DatabricksVectorSearchOptions
{
    public const string SectionName = "DatabricksVectorSearch";

    /// <summary>Base workspace URL, e.g. <c>https://dbc-1234abcd-5678.cloud.databricks.com</c>.</summary>
    public string? WorkspaceUrl { get; set; }

    /// <summary>Databricks personal access token (PAT) used as the bearer token.</summary>
    public string? PersonalAccessToken { get; set; }

    /// <summary>
    /// Fully-qualified Vector Search index name, e.g. <c>main.default.docs_index</c>.
    /// </summary>
    public string? IndexName { get; set; }

    /// <summary>
    /// Columns to return from each match, e.g. <c>["id", "text", "url"]</c>.
    /// Defaults to <c>["id", "text"]</c> when unset.
    /// </summary>
    public string[]? Columns { get; set; }

    /// <summary>Default number of results to return when the tool caller does not specify one.</summary>
    public int NumResults { get; set; } = 5;

    /// <summary>True when the minimum required settings are present to enable the tool.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(WorkspaceUrl)
        && !WorkspaceUrl.Contains("YOUR-WORKSPACE")
        && !string.IsNullOrWhiteSpace(PersonalAccessToken)
        && !string.IsNullOrWhiteSpace(IndexName);
}
