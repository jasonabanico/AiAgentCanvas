#pragma warning disable MEAI001

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Connections;
using AiAgentCanvas.Connectors;
using AiAgentCanvas.Connectors.Mcp;
using Microsoft.Extensions.AI;
using Xunit;

namespace AiAgentCanvas.Tests;

/// <summary>A minimal MCP server over streamable HTTP, answering from memory.</summary>
public sealed class FakeMcpServer
{
    public List<string> Methods { get; } = [];
    public List<(string Name, string Arguments)> Calls { get; } = [];
    public List<string?> Authorization { get; } = [];
    public HttpStatusCode? FailWith { get; set; }

    public (string Name, bool? ReadOnly, bool? Destructive)[] Tools { get; set; } =
    [
        ("search_threads", true, null),
        ("get_thread", null, null),
        ("create_draft", null, null),
        ("send_message", null, null),
        ("delete_thread", null, null),
        ("archiveThread", null, true),
        ("label_thread", null, null),
    ];

    public HttpResponseMessage Handle(HttpRequestMessage request)
    {
        lock (Authorization) Authorization.Add(request.Headers.Authorization?.ToString());

        if (request.Method == HttpMethod.Get)
            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        if (request.Method == HttpMethod.Delete)
            return new HttpResponseMessage(HttpStatusCode.OK);

        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        var message = JsonNode.Parse(body)!;
        var method = message["method"]!.GetValue<string>();
        lock (Methods) Methods.Add(method);

        if (FailWith is { } failure && method != "initialize")
            return new HttpResponseMessage(failure);

        if (message["id"] is null)
            return new HttpResponseMessage(HttpStatusCode.Accepted);

        var id = message["id"]!.DeepClone();
        JsonNode result = method switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = message["params"]!["protocolVersion"]!.DeepClone(),
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = true } },
                ["serverInfo"] = new JsonObject { ["name"] = "fake-mail", ["version"] = "1.0" },
            },
            "tools/list" => new JsonObject
            {
                ["tools"] = new JsonArray(Tools.Select(t =>
                {
                    var tool = new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = $"Does {t.Name}.",
                        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
                    };
                    if (t.ReadOnly is not null || t.Destructive is not null)
                    {
                        var annotations = new JsonObject();
                        if (t.ReadOnly is { } ro) annotations["readOnlyHint"] = ro;
                        if (t.Destructive is { } d) annotations["destructiveHint"] = d;
                        tool["annotations"] = annotations;
                    }
                    return (JsonNode)tool;
                }).ToArray()),
            },
            "tools/call" => CallResult(message["params"]!),
            _ => new JsonObject(),
        };

        var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    private JsonNode CallResult(JsonNode parameters)
    {
        var name = parameters["name"]!.GetValue<string>();
        lock (Calls) Calls.Add((name, parameters["arguments"]?.ToJsonString() ?? "{}"));
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"ran {name}" }),
        };
    }
}

public class McpConnectorTests : IDisposable
{
    private readonly FakeMcpServer _server = new();
    private readonly ConnectorsKit _kit;

    public McpConnectorTests()
    {
        _kit = new ConnectorsKit(k => k.Http = new StubHandlerFactory(r => null!));
        _kit.Http = new StubHandlerFactory(r => _server.Handle(r));
        _kit.Rebuild(McpConnectorDefinition.Gmail);
    }

    public void Dispose() => _kit.Dispose();

    private Connection AddGmail(Dictionary<string, string>? settings = null, string id = "g1", AuthKind auth = AuthKind.ApiKey)
    {
        var connection = new Connection
        {
            Id = id,
            ConnectorId = "gmail",
            Label = "work",
            Auth = auth,
            Settings = settings ?? new() { ["endpoint"] = "https://mcp.example.com/mcp" },
        };
        _kit.Connections.Store.Save(connection);
        _kit.Connections.Store.SaveSecret(id, new SecretPayload { ApiKey = "mail-key" });
        return connection;
    }

    private Dictionary<string, AITool> Registered() =>
        _kit.Tools.GetAllTools().Where(t => t.Name.StartsWith("gmail_")).ToDictionary(t => t.Name);

    [Fact]
    public async Task The_servers_tools_appear_under_the_connectors_prefix_with_a_risk_each()
    {
        AddGmail();

        await _kit.Host.ReconcileAsync(default);

        var tools = Registered();
        Assert.Equal(
            ["gmail_archive_thread", "gmail_create_draft", "gmail_delete_thread", "gmail_get_thread",
             "gmail_label_thread", "gmail_search_threads", "gmail_send_message"],
            tools.Keys.OrderBy(k => k));
    }

    [Theory]
    [InlineData("gmail_search_threads", ToolRisk.Read)]
    [InlineData("gmail_get_thread", ToolRisk.Read)]
    [InlineData("gmail_create_draft", ToolRisk.Write)]
    [InlineData("gmail_label_thread", ToolRisk.Write)]
    [InlineData("gmail_send_message", ToolRisk.Send)]
    [InlineData("gmail_delete_thread", ToolRisk.Destructive)]
    [InlineData("gmail_archive_thread", ToolRisk.Destructive)]
    public async Task Risk_comes_from_the_tool_name_then_the_servers_hints(string tool, ToolRisk expected)
    {
        var connection = AddGmail();
        var context = new ConnectionContext(
            connection, McpConnectorDefinition.Gmail.Descriptor, "gmail", ["mcp.example.com"],
            _kit.Connections.Credentials, _kit.Http, TimeSpan.FromSeconds(5),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await using var connector = (IToolConnector)McpConnectorDefinition.Gmail.Create(context);
        await connector.StartAsync(default);

        var tools = (await connector.GetToolsAsync(default)).ToDictionary(t => t.Name);

        Assert.Equal(expected, ConnectorTools.RiskOf(tools[tool]));
    }

    [Fact]
    public async Task Tools_that_send_or_delete_wait_for_approval_and_others_do_not()
    {
        AddGmail();
        await _kit.Host.ReconcileAsync(default);
        var tools = Registered();

        Assert.IsType<ApprovalRequiredAIFunction>(tools["gmail_send_message"]);
        Assert.IsType<ApprovalRequiredAIFunction>(tools["gmail_delete_thread"]);
        Assert.IsType<ApprovalRequiredAIFunction>(tools["gmail_archive_thread"]);
        Assert.IsNotType<ApprovalRequiredAIFunction>(tools["gmail_search_threads"]);
        Assert.IsNotType<ApprovalRequiredAIFunction>(tools["gmail_create_draft"]);
    }

    [Fact]
    public async Task A_setting_can_raise_a_tool_to_require_approval()
    {
        AddGmail(new() { ["endpoint"] = "https://mcp.example.com/mcp", ["risk.create_draft"] = "Send" });

        await _kit.Host.ReconcileAsync(default);

        Assert.IsType<ApprovalRequiredAIFunction>(Registered()["gmail_create_draft"]);
    }

    [Fact]
    public async Task A_setting_cannot_be_misspelled_into_silence()
    {
        AddGmail(new() { ["endpoint"] = "https://mcp.example.com/mcp", ["risk.create_draft"] = "Sned" });

        await _kit.Host.ReconcileAsync(default);

        var info = Assert.Single(_kit.Host.Snapshot());
        Assert.Equal(ConnectorState.Error, info.State);
        Assert.Contains("risk.create_draft", info.Detail);
        Assert.Empty(Registered());
    }

    [Fact]
    public async Task The_include_list_limits_which_tools_are_exposed()
    {
        AddGmail(new() { ["endpoint"] = "https://mcp.example.com/mcp", ["include"] = "search_threads, get_thread" });

        await _kit.Host.ReconcileAsync(default);

        Assert.Equal(["gmail_get_thread", "gmail_search_threads"], Registered().Keys.OrderBy(k => k));
    }

    [Fact]
    public async Task Calling_a_tool_reaches_the_server_by_its_own_name_with_the_stored_credential()
    {
        AddGmail();
        await _kit.Host.ReconcileAsync(default);
        var tool = (AIFunction)Registered()["gmail_search_threads"];

        var result = await tool.InvokeAsync(new AIFunctionArguments());

        Assert.Equal("search_threads", Assert.Single(_server.Calls).Name);
        Assert.Contains("ran search_threads", result!.ToString());
        Assert.All(_server.Authorization, a => Assert.Equal("Bearer mail-key", a));
    }

    [Fact]
    public async Task An_endpoint_that_is_not_https_fails_at_start()
    {
        AddGmail(new() { ["endpoint"] = "http://mcp.example.com/mcp" });

        await _kit.Host.ReconcileAsync(default);

        var info = Assert.Single(_kit.Host.Snapshot());
        Assert.Equal(ConnectorState.Error, info.State);
        Assert.Contains("endpoint", info.Detail);
    }

    [Fact]
    public async Task Calls_may_reach_only_the_servers_own_host()
    {
        AddGmail();
        var definition = McpConnectorDefinition.Gmail;

        Assert.Equal(["mcp.example.com"], definition.AllowedHostsFor(new Dictionary<string, string> { ["endpoint"] = "https://mcp.example.com/mcp" }));
        Assert.Empty(definition.AllowedHostsFor(new Dictionary<string, string>()));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_healthy_server_checks_out_and_one_that_refuses_the_credential_asks_for_reauthorisation()
    {
        AddGmail();
        await _kit.Host.ReconcileAsync(default);

        Assert.Equal(ConnectorState.Connected, (await _kit.Host.CheckNowAsync("g1", default))!.State);

        _server.FailWith = HttpStatusCode.Unauthorized;
        Assert.Equal(ConnectorState.NeedsReauth, (await _kit.Host.CheckNowAsync("g1", default))!.State);
    }

    [Fact]
    public async Task A_server_that_lost_the_session_is_reconnected_and_its_tools_fetched_again()
    {
        AddGmail();
        await _kit.Host.ReconcileAsync(default);
        var initializesBefore = _server.Methods.Count(m => m == "initialize");

        _server.FailWith = HttpStatusCode.InternalServerError;
        var down = await _kit.Host.CheckNowAsync("g1", default);
        Assert.Equal(ConnectorState.Error, down!.State);

        _server.FailWith = null;
        var up = await _kit.Host.CheckNowAsync("g1", default);

        Assert.Equal(ConnectorState.Connected, up!.State);
        Assert.True(_server.Methods.Count(m => m == "initialize") > initializesBefore);
        await Task.Delay(200);
        Assert.NotEmpty(Registered());
    }
}

public class McpNamingTests
{
    [Theory]
    [InlineData("search_threads", "search_threads")]
    [InlineData("archiveThread", "archive_thread")]
    [InlineData("Get-Thread.v2", "get_thread_v2")]
    [InlineData("9lives", "t_9lives")]
    public void A_server_tool_name_becomes_a_valid_model_tool_name(string remote, string expected) =>
        Assert.Equal(expected, McpConnector.Normalize(remote));
}
