using AiAgentCanvas.Core.Configuration;
using Azure;
using Azure.AI.Inference;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AiAgentCanvas.Core.Services;

public sealed class AIFoundryClientFactory
{
    private readonly AIFoundryOptions _options;

    public AIFoundryClientFactory(IOptions<AIFoundryOptions> options)
    {
        _options = options.Value;
    }

    public IChatClient CreateChatClient()
    {
        var endpoint = new Uri(_options.Endpoint);
        var inner = _options.UseAzureCredential
            ? new ChatCompletionsClient(endpoint, new DefaultAzureCredential())
            : new ChatCompletionsClient(endpoint, new AzureKeyCredential(
                _options.Key ?? throw new InvalidOperationException("AIFoundry:Key is required when UseAzureCredential is false.")));

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
