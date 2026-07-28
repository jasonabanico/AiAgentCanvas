namespace AiAgentCanvas.Providers.Snowflake;

/// <summary>
/// Configuration for the Snowflake Cortex provider. Cortex exposes an OpenAI-compatible
/// Chat Completions API at <c>{AccountUrl}/api/v2/cortex/v1/chat/completions</c>, so the
/// standard OpenAI client is pointed at <c>{AccountUrl}/api/v2/cortex/v1</c> and authenticated
/// with a Snowflake Programmatic Access Token (PAT) using the <c>pat/&lt;token&gt;</c> bearer scheme.
/// </summary>
public sealed class SnowflakeOptions
{
    public const string SectionName = "Snowflake";

    /// <summary>
    /// Base account URL, e.g. <c>https://myorg-myaccount.snowflakecomputing.com</c>
    /// (account identifier is <c>orgname-accountname</c>). The provider appends
    /// <c>/api/v2/cortex/v1</c> to build the OpenAI-compatible base URL.
    /// </summary>
    public required string AccountUrl { get; set; }

    /// <summary>
    /// Snowflake Programmatic Access Token (PAT). Generate in Snowsight under
    /// User menu → My profile → Programmatic access tokens. A JWT/OAuth token also works
    /// (see <see cref="BuildAuthToken"/> for how the bearer value is formed).
    /// </summary>
    public string? PersonalAccessToken { get; set; }

    /// <summary>
    /// Cortex model name, e.g. <c>claude-sonnet-4-5</c>, <c>llama3.1-70b</c>,
    /// <c>mistral-large2</c>, or <c>snowflake-arctic</c>.
    /// </summary>
    public required string ModelName { get; set; }

    /// <summary>
    /// Optional Cortex embeddings model. NOTE: Cortex embeddings are not confirmed to be
    /// OpenAI-<c>/embeddings</c>-compatible (they historically go through EMBED_TEXT SQL
    /// functions), so this path is opt-in and not wired into host RAG by default.
    /// </summary>
    public string? EmbeddingModelName { get; set; }

    /// <summary>Builds the OpenAI-compatible base URL: <c>{AccountUrl}/api/v2/cortex/v1</c>.</summary>
    public Uri BuildCortexEndpointUri()
    {
        var trimmed = (AccountUrl ?? string.Empty).TrimEnd('/');
        return new Uri($"{trimmed}/api/v2/cortex/v1");
    }

    /// <summary>
    /// Forms the bearer value. Cortex PATs authenticate as <c>pat/&lt;token&gt;</c>; a token that
    /// already carries a scheme prefix (contains '/') is passed through unchanged (e.g. JWT/OAuth).
    /// </summary>
    public string BuildAuthToken()
    {
        var token = PersonalAccessToken ?? string.Empty;
        return token.Contains('/') ? token : $"pat/{token}";
    }
}
