using AiAgentCanvas.Core.Configuration;
using Azure;
using Azure.AI.Inference;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentCanvas.Core.Services;

public sealed class AIFoundryClientFactory
{
    private readonly AIFoundryOptions _options;
    private readonly ILogger<AIFoundryClientFactory> _logger;

    public AIFoundryClientFactory(IOptions<AIFoundryOptions> options, ILogger<AIFoundryClientFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IChatClient CreateChatClient()
    {
        _logger.LogInformation("Creating chat client. Endpoint={Endpoint}, Deployment={Deployment}, UseAzureCredential={UseAzure}, HasKey={HasKey}",
            _options.Endpoint, _options.DeploymentName, _options.UseAzureCredential, !string.IsNullOrWhiteSpace(_options.Key));

        if (string.IsNullOrWhiteSpace(_options.Endpoint) || _options.Endpoint.Contains("YOUR-RESOURCE"))
            throw new InvalidOperationException(
                $"AIFoundry:Endpoint is not configured (got: '{_options.Endpoint}'). Set a valid Azure AI Foundry endpoint in appsettings.json or appsettings.Development.json.");

        if (!_options.UseAzureCredential && string.IsNullOrWhiteSpace(_options.Key))
            throw new InvalidOperationException(
                "AIFoundry:Key is required when UseAzureCredential is false. Set it in appsettings.json.");

        var endpoint = new Uri(_options.Endpoint);
        var inner = _options.UseAzureCredential
            ? new ChatCompletionsClient(endpoint, new DefaultAzureCredential())
            : new ChatCompletionsClient(endpoint, new AzureKeyCredential(_options.Key!));

        return inner.AsIChatClient(_options.DeploymentName);
    }

    public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator()
    {
        if (string.IsNullOrEmpty(_options.EmbeddingDeploymentName))
            return null;

        var endpoint = new Uri(_options.Endpoint);
        var inner = _options.UseAzureCredential
            ? new EmbeddingsClient(endpoint, new DefaultAzureCredential())
            : new EmbeddingsClient(endpoint, new AzureKeyCredential(
                _options.Key ?? throw new InvalidOperationException("AIFoundry:Key is required when UseAzureCredential is false.")));

        return inner.AsIEmbeddingGenerator(_options.EmbeddingDeploymentName);
    }
}
