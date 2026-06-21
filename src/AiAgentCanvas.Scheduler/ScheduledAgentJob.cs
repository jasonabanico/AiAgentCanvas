using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Scheduler;

public sealed class ScheduledAgentJob
{
    private readonly AIAgent _agent;
    private readonly ScheduledTaskStore _store;
    private readonly INotificationSink? _notificationSink;
    private readonly ILogger<ScheduledAgentJob> _logger;

    public ScheduledAgentJob(AIAgent agent, ScheduledTaskStore store, ILogger<ScheduledAgentJob> logger, INotificationSink? notificationSink = null)
    {
        _agent = agent;
        _store = store;
        _notificationSink = notificationSink;
        _logger = logger;
    }

    public async Task ExecuteAsync(string taskId, string description, string prompt)
    {
        _logger.LogInformation("Executing scheduled task {TaskId}: {Description}", taskId, description);

        var session = await _agent.CreateSessionAsync();
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };

        var response = await _agent.RunAsync(messages, session);
        var resultText = response.Text ?? "(no response)";

        _store.SaveResult(taskId, description, resultText);
        _logger.LogInformation("Scheduled task {TaskId} completed. Result length: {Length}", taskId, resultText.Length);

        if (_notificationSink is not null)
        {
            await _notificationSink.SendAsync(new AgentNotification
            {
                Title = $"Task completed: {description}",
                Body = resultText.Length > 500 ? resultText[..500] + "..." : resultText,
                Source = $"scheduler:{taskId}",
            });
        }
    }
}
