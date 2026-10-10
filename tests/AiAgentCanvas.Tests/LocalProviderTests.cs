using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Providers.Local;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiAgentCanvas.Tests;

public class LocalAddressTests
{
    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.1:8080/v1")]
    [InlineData("http://[::1]:1234/v1")]
    [InlineData("http://10.0.4.7:8000/v1")]
    [InlineData("http://172.16.0.9/v1")]
    [InlineData("http://172.31.255.1/v1")]
    [InlineData("http://192.168.1.50:11434/v1")]
    [InlineData("http://100.101.102.103:11434/v1")]
    [InlineData("http://169.254.10.10/v1")]
    [InlineData("http://mac-studio:11434/v1")]
    [InlineData("http://spark.local:8000/v1")]
    [InlineData("http://gpu-box.lan/v1")]
    [InlineData("http://models.internal/v1")]
    [InlineData("http://[fd12:3456::1]:8000/v1")]
    public void Addresses_inside_the_machine_or_a_private_network_count_as_local(string url) =>
        Assert.True(LocalModelOptions.IsLocalAddress(new Uri(url)));

    [Theory]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("http://8.8.8.8:8000/v1")]
    [InlineData("http://172.32.0.1/v1")]
    [InlineData("http://100.128.0.1/v1")]
    [InlineData("http://example.com/v1")]
    [InlineData("http://models.example.org:8000/v1")]
    public void Public_addresses_do_not(string url) =>
        Assert.False(LocalModelOptions.IsLocalAddress(new Uri(url)));
}

public class LocalModelClientFactoryTests
{
    private static LocalModelClientFactory Factory(Action<LocalModelOptions> configure)
    {
        var options = new LocalModelOptions { ModelName = "gpt-oss:120b" };
        configure(options);
        return new LocalModelClientFactory(Options.Create(options), NullLogger<LocalModelClientFactory>.Instance);
    }

    [Fact]
    public void A_local_endpoint_and_a_model_name_give_a_chat_client()
    {
        using var client = Factory(_ => { }).CreateChatClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void A_public_endpoint_is_refused_and_the_message_says_why()
    {
        var factory = Factory(o => o.Endpoint = "https://api.example.com/v1");

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateChatClient());
        Assert.Contains("outside this machine", ex.Message);
        Assert.Contains("AllowNonLocalEndpoint", ex.Message);
    }

    [Fact]
    public void A_public_endpoint_is_accepted_when_the_operator_allows_it()
    {
        var factory = Factory(o => { o.Endpoint = "https://api.example.com/v1"; o.AllowNonLocalEndpoint = true; });

        using var client = factory.CreateChatClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void A_missing_model_name_is_reported_by_name()
    {
        var factory = Factory(o => o.ModelName = null);

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateChatClient());
        Assert.Contains("Local:ModelName", ex.Message);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://localhost/v1")]
    public void An_endpoint_that_is_not_an_http_url_is_refused(string endpoint)
    {
        var factory = Factory(o => o.Endpoint = endpoint);

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateChatClient());
        Assert.Contains("Local:Endpoint", ex.Message);
    }

    [Fact]
    public void Embeddings_are_off_until_a_model_is_named()
    {
        Assert.Null(Factory(_ => { }).CreateEmbeddingGenerator());

        using var generator = Factory(o => o.EmbeddingModelName = "nomic-embed-text").CreateEmbeddingGenerator();
        Assert.NotNull(generator);
    }
}

public class LocalModelRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalModel(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_chat_client_and_the_configured_keyed_clients_are_registered()
    {
        using var provider = Build(new()
        {
            ["Local:ModelName"] = "gpt-oss:120b",
            ["Local:EconomyModelName"] = "qwen3.5:9b",
            ["Local:JudgeModelName"] = "nemotron:12b",
        });

        Assert.NotNull(provider.GetRequiredService<IChatClient>());
        Assert.NotNull(provider.GetKeyedService<IChatClient>(AgentClientKeys.Economy));
        Assert.NotNull(provider.GetKeyedService<IChatClient>(AgentClientKeys.Judge));
    }

    [Fact]
    public void A_keyed_client_that_is_not_configured_is_not_registered()
    {
        using var provider = Build(new() { ["Local:ModelName"] = "gpt-oss:120b" });

        Assert.Null(provider.GetKeyedService<IChatClient>(AgentClientKeys.Economy));
        Assert.Null(provider.GetKeyedService<IChatClient>(AgentClientKeys.Judge));
    }
}
