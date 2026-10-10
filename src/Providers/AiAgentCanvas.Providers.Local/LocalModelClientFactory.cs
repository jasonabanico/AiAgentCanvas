using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace AiAgentCanvas.Providers.Local;

/// <summary>
/// Creates chat and embedding clients for a local OpenAI-compatible server. The plain
/// <see cref="OpenAIClient"/> is pointed at the configured endpoint and bridged with
/// <c>.AsIChatClient()</c>, the same as the Databricks provider, so everything downstream of
/// <see cref="IChatClient"/> is unchanged.
/// </summary>
public sealed class LocalModelClientFactory
{
    private readonly LocalModelOptions _options;
    private readonly ILogger<LocalModelClientFactory> _logger;

    public LocalModelClientFactory(IOptions<LocalModelOptions> options, ILogger<LocalModelClientFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IChatClient CreateChatClient() => CreateChatClient(null);

    public IChatClient CreateChatClient(string? modelName)
    {
        var model = modelName ?? _options.ModelName;
        var endpoint = Validate(model);

        _logger.LogInformation("Creating local chat client. Endpoint={Endpoint}, Model={Model}", endpoint, model);
        return CreateOpenAIClient(endpoint).GetChatClient(model).AsIChatClient();
    }

    public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator()
    {
        if (string.IsNullOrWhiteSpace(_options.EmbeddingModelName))
            return null;

        var endpoint = Validate(_options.EmbeddingModelName);
        return CreateOpenAIClient(endpoint).GetEmbeddingClient(_options.EmbeddingModelName).AsIEmbeddingGenerator();
    }

    private Uri Validate(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(
                "Local:ModelName is required: the model tag the server knows, for example gpt-oss:120b.");

        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"Local:Endpoint must be an http or https URL such as http://localhost:11434/v1 (got: '{_options.Endpoint}').");
        }

        if (!_options.AllowNonLocalEndpoint && !LocalModelOptions.IsLocalAddress(endpoint))
        {
            throw new InvalidOperationException(
                $"Local:Endpoint '{endpoint.Host}' is outside this machine and the private networks. "
                + "Prompts, tool results and documents would leave the network. "
                + "Point it at a local server, or set Local:AllowNonLocalEndpoint to true if that is intended.");
        }

        return endpoint;
    }

    private OpenAIClient CreateOpenAIClient(Uri endpoint)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = endpoint,
            NetworkTimeout = TimeSpan.FromSeconds(Math.Max(10, _options.RequestTimeoutSeconds)),
        };
        return new OpenAIClient(new ApiKeyCredential(_options.ApiKey), clientOptions);
    }
}
