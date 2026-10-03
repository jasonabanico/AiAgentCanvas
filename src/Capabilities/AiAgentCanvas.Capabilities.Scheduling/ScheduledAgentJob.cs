using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Scheduling;

public sealed class ScheduledAgentJob
{
    private readonly IServiceProvider _sp;
    private readonly IScheduledTaskStore _store;
    private readonly INotificationSink? _notificationSink;
    private readonly ILogger<ScheduledAgentJob> _logger;

    public ScheduledAgentJob(IServiceProvider sp, IScheduledTaskStore store, ILogger<ScheduledAgentJob> logger, INotificationSink? notificationSink = null)
    {
        _sp = sp;
        _store = store;
        _notificationSink = notificationSink;
        _logger = logger;
    }

    /// <summary>The agent scheduled tasks run on, which a spend limit is checked against.</summary>
    public string AgentName => _sp.GetService<AIAgent>()?.Name ?? "default";

    public async Task ExecuteAsync(string taskId, string description, string prompt, CancellationToken ct = default)
    {
        _logger.LogInformation("Executing scheduled task {TaskId}: {Description}", taskId, description);

        var agent = _sp.GetRequiredService<AIAgent>();

        var resultText = await RunTracking.RunAsync(
            _sp.GetService<IRunLedger>(),
            new RunStart(RunSource.Scheduled, agent.Name ?? "default", prompt, TaskId: taskId),
            async token =>
            {
                var session = await agent.CreateSessionAsync(token);
                var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
                var response = await agent.RunAsync(messages, session, cancellationToken: token);
                return response.Text ?? "(no response)";
            },
            ct,
            ex => _logger.LogWarning(ex, "Run ledger write failed for task {TaskId}", taskId));

        _store.SaveResult(taskId, description, resultText);
        _logger.LogInformation("Scheduled task {TaskId} completed. Result length: {Length}", taskId, resultText.Length);

        if (_notificationSink is not null)
        {
            _logger.LogInformation("Sending notification for task {TaskId}", taskId);
            await _notificationSink.SendAsync(new AgentNotification
            {
                Title = $"Task completed: {description}",
                Body = resultText.Length > 500 ? resultText[..500] + "..." : resultText,
                Source = $"scheduler:{taskId}",
            }, ct);
        }
        else
        {
            _logger.LogWarning("No notification sink available for task {TaskId}", taskId);
        }
    }
}
