#pragma warning disable MEAI001

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Workflows;

public sealed class DeclarativeWorkflowExecutor
{
    private readonly string _workflowDirectory;
    private readonly IServiceProvider _sp;
    private readonly ILogger<DeclarativeWorkflowExecutor> _logger;

    public DeclarativeWorkflowExecutor(string workflowDirectory, IServiceProvider sp, ILogger<DeclarativeWorkflowExecutor> logger)
    {
        _workflowDirectory = workflowDirectory;
        _sp = sp;
        _logger = logger;

        if (!Directory.Exists(_workflowDirectory))
            Directory.CreateDirectory(_workflowDirectory);
    }

    public IReadOnlyList<string> ListDeclarativeWorkflows()
    {
        return Directory.GetFiles(_workflowDirectory, "*.yaml")
            .Concat(Directory.GetFiles(_workflowDirectory, "*.yml"))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Cast<string>()
            .Order()
            .ToList();
    }

    public async Task<string> ExecuteAsync(string workflowFileName, string userInput, CancellationToken ct)
    {
        var filePath = ResolveWorkflowFile(workflowFileName);
        if (filePath is null)
            return JsonSerializer.Serialize(new { error = $"Declarative workflow '{workflowFileName}' not found in {_workflowDirectory}" });

        _logger.LogInformation("Executing declarative workflow from {File}", filePath);

        var chatClient = _sp.GetRequiredService<IChatClient>();
        var configuration = _sp.GetService<IConfiguration>();
        var agentProvider = new ChatClientAgentProvider(chatClient);
        var options = new DeclarativeWorkflowOptions(agentProvider)
        {
            Configuration = configuration,
        };

        var workflow = DeclarativeWorkflowBuilder.Build<ChatMessage>(filePath, options);
        var inputMessage = new ChatMessage(ChatRole.User, userInput);

        await using var run = await InProcessExecution.RunAsync(workflow, inputMessage, cancellationToken: ct);
        var events = run.OutgoingEvents.ToList();

        var results = events
            .OfType<AgentResponseEvent>()
            .Select(e => new { agent = e.ExecutorId, response = e.Response.Text })
            .ToList();

        _logger.LogInformation("Declarative workflow {File} completed with {Count} events", workflowFileName, results.Count);

        return JsonSerializer.Serialize(new { type = "declarative", workflow = workflowFileName, results });
    }

    private string? ResolveWorkflowFile(string name)
    {
        var yamlPath = Path.Combine(_workflowDirectory, name.EndsWith(".yaml") || name.EndsWith(".yml") ? name : name + ".yaml");
        if (File.Exists(yamlPath)) return yamlPath;

        var ymlPath = Path.Combine(_workflowDirectory, name + ".yml");
        if (File.Exists(ymlPath)) return ymlPath;

        return null;
    }
}

internal sealed class ChatClientAgentProvider : ResponseAgentProvider
{
    private readonly IChatClient _chatClient;
    private int _conversationCounter;

    public ChatClientAgentProvider(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public override Task<string> CreateConversationAsync(CancellationToken cancellationToken = default)
    {
        var id = $"conv-{Interlocked.Increment(ref _conversationCounter)}";
        return Task.FromResult(id);
    }

    public override async Task<ChatMessage> CreateMessageAsync(string conversationId, ChatMessage conversationMessage, CancellationToken cancellationToken = default)
    {
        var response = await _chatClient.GetResponseAsync([conversationMessage], cancellationToken: cancellationToken);
        return new ChatMessage(ChatRole.Assistant, response.Text);
    }

    public override Task<ChatMessage> GetMessageAsync(string conversationId, string messageId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatMessage(ChatRole.Assistant, string.Empty));
    }

    public override async IAsyncEnumerable<AgentResponseUpdate> InvokeAgentAsync(
        string agentId,
        string? agentVersion,
        string? conversationId,
        IEnumerable<ChatMessage>? messages,
        IDictionary<string, object?>? inputArguments,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages?.ToList() ?? [];
        var response = await _chatClient.GetResponseAsync(messageList, cancellationToken: cancellationToken);

        var update = new AgentResponseUpdate();
        update.Contents.Add(new TextContent(response.Text ?? string.Empty));
        yield return update;
    }

    public override async IAsyncEnumerable<ChatMessage> GetMessagesAsync(
        string conversationId,
        int? limit = null,
        string? after = null,
        string? before = null,
        bool newestFirst = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
