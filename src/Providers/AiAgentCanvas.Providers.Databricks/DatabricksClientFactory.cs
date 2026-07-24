using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace AiAgentCanvas.Providers.Databricks;

/// <summary>
/// Creates <see cref="IChatClient"/> / embedding generators backed by Databricks
/// Foundation Model APIs. Databricks serving endpoints speak the OpenAI wire
/// protocol, so we use the plain <see cref="OpenAIClient"/> (not Azure.AI.OpenAI)
/// with a custom endpoint and a Databricks PAT, then bridge via <c>.AsIChatClient()</c> —
/// exactly like the Azure AI Foundry provider, so everything downstream is unchanged.
/// </summary>
public sealed class DatabricksClientFactory
{
    private readonly DatabricksOptions _options;
    private readonly ILogger<DatabricksClientFactory> _logger;

    public DatabricksClientFactory(IOptions<DatabricksOptions> options, ILogger<DatabricksClientFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IChatClient CreateChatClient()
    {
        _logger.LogInformation("Creating Databricks chat client. WorkspaceUrl={WorkspaceUrl}, Model={Model}, HasToken={HasToken}",
            _options.WorkspaceUrl, _options.ModelName, !string.IsNullOrWhiteSpace(_options.PersonalAccessToken));

        Validate();

        var client = CreateOpenAIClient();
        return client.GetChatClient(_options.ModelName).AsIChatClient();
    }

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
        if (string.IsNullOrWhiteSpace(_options.WorkspaceUrl) || _options.WorkspaceUrl.Contains("YOUR-WORKSPACE"))
            throw new InvalidOperationException(
                $"Databricks:WorkspaceUrl is not configured (got: '{_options.WorkspaceUrl}'). " +
                "Set your workspace URL (e.g. https://dbc-xxxx.cloud.databricks.com) in appsettings.json.");

        if (string.IsNullOrWhiteSpace(_options.PersonalAccessToken))
            throw new InvalidOperationException(
                "Databricks:PersonalAccessToken is required. Generate a PAT in the Databricks workspace and set it in appsettings.json.");

        if (string.IsNullOrWhiteSpace(_options.ModelName))
            throw new InvalidOperationException(
                "Databricks:ModelName is required (the serving endpoint name, e.g. databricks-dbrx-instruct).");
    }

    private OpenAIClient CreateOpenAIClient()
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = _options.BuildServingEndpointUri() };
        return new OpenAIClient(new ApiKeyCredential(_options.PersonalAccessToken!), clientOptions);
    }
}
