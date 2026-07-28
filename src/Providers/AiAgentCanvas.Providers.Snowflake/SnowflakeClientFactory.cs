using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace AiAgentCanvas.Providers.Snowflake;

/// <summary>
/// Creates <see cref="IChatClient"/> backed by Snowflake Cortex. Cortex's Chat Completions API
/// follows the OpenAI wire protocol, so we use the plain <see cref="OpenAIClient"/> (not
/// Azure.AI.OpenAI) with a custom endpoint and a Snowflake PAT, then bridge via
/// <c>.AsIChatClient()</c> — exactly like the Databricks and Azure AI Foundry providers, so
/// everything downstream (agents, workflows, tools, context providers) is unchanged.
/// </summary>
public sealed class SnowflakeClientFactory
{
    private readonly SnowflakeOptions _options;
    private readonly ILogger<SnowflakeClientFactory> _logger;

    public SnowflakeClientFactory(IOptions<SnowflakeOptions> options, ILogger<SnowflakeClientFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IChatClient CreateChatClient()
    {
        _logger.LogInformation("Creating Snowflake Cortex chat client. AccountUrl={AccountUrl}, Model={Model}, HasToken={HasToken}",
            _options.AccountUrl, _options.ModelName, !string.IsNullOrWhiteSpace(_options.PersonalAccessToken));

        Validate();

        var client = CreateOpenAIClient();
        return client.GetChatClient(_options.ModelName).AsIChatClient();
    }

    /// <summary>
    /// Opt-in Cortex embeddings. NOTE: Cortex embeddings are not confirmed OpenAI-compatible;
    /// this uses the same OpenAI SDK path and may not resolve against every account. The host
    /// does not wire this into RAG by default. Returns null when no embedding model is configured.
    /// </summary>
    public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator()
    {
        if (string.IsNullOrWhiteSpace(_options.EmbeddingModelName))
            return null;

        Validate();

        var client = CreateOpenAIClient();
        return client.GetEmbeddingClient(_options.EmbeddingModelName).AsIEmbeddingGenerator();
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountUrl) || _options.AccountUrl.Contains("YOUR-ACCOUNT"))
            throw new InvalidOperationException(
                $"Snowflake:AccountUrl is not configured (got: '{_options.AccountUrl}'). " +
                "Set your account URL (e.g. https://myorg-myaccount.snowflakecomputing.com) in appsettings.json.");

        if (string.IsNullOrWhiteSpace(_options.PersonalAccessToken))
            throw new InvalidOperationException(
                "Snowflake:PersonalAccessToken is required. Generate a PAT in Snowsight (My profile → Programmatic access tokens) and set it in appsettings.json.");

        if (string.IsNullOrWhiteSpace(_options.ModelName))
            throw new InvalidOperationException(
                "Snowflake:ModelName is required (a Cortex model, e.g. claude-sonnet-4-5 or llama3.1-70b).");
    }

    private OpenAIClient CreateOpenAIClient()
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = _options.BuildCortexEndpointUri() };
        return new OpenAIClient(new ApiKeyCredential(_options.BuildAuthToken()), clientOptions);
    }
}
