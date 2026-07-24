namespace AiAgentCanvas.Providers.Databricks;

/// <summary>
/// Configuration for the Databricks Foundation Model APIs provider.
/// Databricks serving endpoints are OpenAI-compatible, so the standard OpenAI
/// client is pointed at <c>{WorkspaceUrl}/serving-endpoints</c> and authenticated
/// with a Databricks personal access token (PAT).
/// </summary>
public sealed class DatabricksOptions
{
    public const string SectionName = "Databricks";

    /// <summary>
    /// Base workspace URL, e.g. <c>https://dbc-1234abcd-5678.cloud.databricks.com</c>.
    /// The provider appends <c>/serving-endpoints</c> to build the OpenAI-compatible base URL.
    /// </summary>
    public required string WorkspaceUrl { get; set; }

    /// <summary>Databricks personal access token (PAT) used as the OpenAI API key.</summary>
    public string? PersonalAccessToken { get; set; }

    /// <summary>
    /// Serving endpoint / model name, e.g. <c>databricks-dbrx-instruct</c>,
    /// <c>databricks-meta-llama-3-3-70b-instruct</c>, or a pay-per-token Claude/GPT endpoint.
    /// </summary>
    public required string ModelName { get; set; }

    /// <summary>
    /// Optional serving endpoint that exposes an embeddings model, e.g.
    /// <c>databricks-bge-large-en</c>. When unset, embedding generation is disabled.
    /// </summary>
    public string? EmbeddingModelName { get; set; }

    /// <summary>Builds the OpenAI-compatible base URL: <c>{WorkspaceUrl}/serving-endpoints</c>.</summary>
    public Uri BuildServingEndpointUri()
    {
        var trimmed = (WorkspaceUrl ?? string.Empty).TrimEnd('/');
        return new Uri($"{trimmed}/serving-endpoints");
    }
}
