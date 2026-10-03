using System.Net;
using System.Text;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.EventTriggers;
using AiAgentCanvas.Connections;
using AiAgentCanvas.Connectors;
using AiAgentCanvas.Connectors.Mcp;
using AiAgentCanvas.Connectors.TwilioSms;
using AiAgentCanvas.Orchestration.Skills;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

/// <summary>
/// A text arrives at Twilio, Twilio posts a signed webhook, and a trigger queues an agent
/// run, through the real endpoint, host, connector and event bridge.
/// </summary>
public class ConnectorEndToEndTests : TriggerTestBase, IDisposable
{
    private const string PublicBase = "https://agents.example.com";
    private readonly ConnectionsKit _connections = new(o => o.PublicBaseUrl = PublicBase);

    public new void Dispose()
    {
        base.Dispose();
        _connections.Dispose();
    }

    private sealed record Rig(WebApplication App, HttpClient Client, TriggerEventQueue Queue, TriggerRegistry Registry, ConnectorHost Host);

    private async Task<Rig> StartAsync()
    {
        var store = NewStore();
        var registry = new TriggerRegistry(NullLogger<TriggerRegistry>.Instance, store);
        var queue = NewQueue(store);
        var bridge = new ConnectorEventBridge(registry, queue, NullLogger<ConnectorEventBridge>.Instance);

        _connections.Store.Save(new Connection
        {
            Id = "tw1",
            ConnectorId = "twilio-sms",
            Label = "front desk",
            Auth = AuthKind.KeyPair,
            Settings = { ["account_sid"] = TwilioFixture.AccountSid, ["from_number"] = "+14155550100" },
        });
        _connections.Store.SaveSecret("tw1", new SecretPayload
        {
            KeyId = "SK1",
            KeySecret = "key-secret",
            Extras = { ["AuthToken"] = TwilioFixture.AuthToken },
        });

        var options = new ConnectorOptions { MaxWebhookBodyBytes = 2048 };
        var host = new ConnectorHost(
            new ConnectorRegistry([new TwilioSmsConnectorDefinition()]),
            _connections.Store, _connections.Credentials,
            new StubHandlerFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            new DynamicToolRegistry(), new ServiceCollection().BuildServiceProvider(),
            options, NullLoggerFactory.Instance, bridge, store);
        await host.ReconcileAsync(default);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(_connections.Options);
        builder.Services.AddSingleton(host);
        builder.Services.AddSingleton(new ConnectorRegistry([new TwilioSmsConnectorDefinition()]));
        var app = builder.Build();
        app.MapConnectorWebhook();
        app.MapConnectorEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new Rig(app, new HttpClient { BaseAddress = new Uri(address) }, queue, registry, host);
    }

    private static (StringContent Content, string Signature) SignedText(string sid, string body, string token = TwilioFixture.AuthToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("MessageSid", sid), new("From", "+14155550123"), new("To", "+14155550100"), new("Body", body), new("NumMedia", "0"),
        };
        var encoded = string.Join('&', fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
        var signature = TwilioSmsConnector.Sign(token, $"{PublicBase}/api/connectors/tw1/webhook", fields);
        return (new StringContent(encoded, Encoding.UTF8, "application/x-www-form-urlencoded"), signature);
    }

    private static async Task<HttpResponseMessage> PostAsync(Rig rig, StringContent content, string? signature, string connection = "tw1")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/connectors/{connection}/webhook") { Content = content };
        if (signature is not null)
            request.Headers.Add("X-Twilio-Signature", signature);
        return await rig.Client.SendAsync(request);
    }

    private static void WatchTexts(Rig rig) => rig.Registry.Register(new EventTrigger
    {
        Id = "reply",
        Name = "reply to texts",
        Type = EventTriggerType.Connector,
        SourceConnection = "tw1",
        SourceEventType = "sms.received",
        AgentMessage = "Draft a reply.",
    });

    [Fact]
    public async Task A_signed_text_queues_one_agent_run_and_a_redelivery_does_not_queue_another()
    {
        var rig = await StartAsync();
        await using var _ = rig.App;
        WatchTexts(rig);
        var (content, signature) = SignedText("SM1", "Can I move my booking?");

        var first = await PostAsync(rig, content, signature);
        var (again, sameSignature) = SignedText("SM1", "Can I move my booking?");
        var second = await PostAsync(rig, again, sameSignature);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, rig.Queue.Counts().Pending);
        var queued = rig.Queue.ClaimNext(DateTimeOffset.UtcNow)!;
        Assert.Contains("Can I move my booking?", queued.Message);
        Assert.Equal("tw1", queued.Metadata["connectionId"]);
    }

    [Fact]
    public async Task A_webhook_signed_with_the_wrong_token_is_refused_and_queues_nothing()
    {
        var rig = await StartAsync();
        await using var _ = rig.App;
        WatchTexts(rig);
        var (content, signature) = SignedText("SM1", "hi", token: "not-the-token");

        var response = await PostAsync(rig, content, signature);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, rig.Queue.Counts().Pending);
    }

    [Fact]
    public async Task A_webhook_with_no_signature_is_refused()
    {
        var rig = await StartAsync();
        await using var _ = rig.App;
        var (content, _) = SignedText("SM1", "hi");

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(rig, content, null)).StatusCode);
    }

    [Fact]
    public async Task A_webhook_for_an_unknown_connection_is_not_found()
    {
        var rig = await StartAsync();
        await using var _ = rig.App;
        var (content, signature) = SignedText("SM1", "hi");

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(rig, content, signature, connection: "nope")).StatusCode);
    }

    [Fact]
    public async Task An_oversized_body_is_refused_before_it_is_verified()
    {
        var rig = await StartAsync();
        await using var _ = rig.App;
        var (_, signature) = SignedText("SM1", "hi");
        var huge = new StringContent("Body=" + new string('x', 4000), Encoding.UTF8, "application/x-www-form-urlencoded");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await PostAsync(rig, huge, signature)).StatusCode);
    }

    [Fact]
    public async Task A_full_backlog_asks_twilio_to_retry_later()
    {
        var rig = await StartAsync();
        await using var _ = rig.App;
        WatchTexts(rig);

        for (var i = 0; i < 16; i++)
        {
            var (c, s) = SignedText($"SM{i}", "hi");
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(rig, c, s)).StatusCode);
        }

        var (content, signature) = SignedText("SMoverflow", "hi");
        var response = await PostAsync(rig, content, signature);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task The_management_endpoint_lists_the_connection_with_its_webhook_address_and_no_secrets()
    {
        var rig = await StartAsync();
        await using var _ = rig.App;

        var json = await rig.Client.GetStringAsync("/api/connectors/connections");

        Assert.Contains($"{PublicBase}/api/connectors/tw1/webhook", json);
        Assert.Contains("twilio_sms", json);
        Assert.DoesNotContain("key-secret", json);
        Assert.DoesNotContain(TwilioFixture.AuthToken, json);
    }

    [Fact]
    public void The_registration_extensions_build_a_host_that_knows_every_shipped_connector()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_connections.Options);
        services.AddSingleton(_connections.Store);
        services.AddSingleton<ICredentialProvider>(_connections.Credentials);
        services.AddSingleton<DynamicToolRegistry>();
        services.AddAiAgentCanvasConnectors(new ConfigurationBuilder().Build());
        services.AddTwilioSmsConnector();
        services.AddGmailMcpConnector();
        services.AddMcpConnector();

        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ConnectorRegistry>();
        Assert.Equal(["gmail", "mcp", "twilio-sms"], registry.List().Select(d => d.Id).OrderBy(i => i));
        Assert.NotNull(provider.GetRequiredService<ConnectorHost>());
    }

    [Fact]
    public void Every_shipped_connector_names_the_hosts_it_may_call()
    {
        Assert.Equal(["api.twilio.com"], new TwilioSmsConnectorDefinition().Descriptor.AllowedHosts);
        Assert.Empty(McpConnectorDefinition.Gmail.AllowedHostsFor(new Dictionary<string, string>()));
    }
}
