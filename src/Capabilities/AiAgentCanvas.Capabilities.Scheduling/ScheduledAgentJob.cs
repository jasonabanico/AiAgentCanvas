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

    public async Task ExecuteAsync(string taskId, string description, string prompt)
    {
        _logger.LogInformation("Executing scheduled task {TaskId}: {Description}", taskId, description);

        var agent = _sp.GetRequiredService<AIAgent>();
        var session = await agent.CreateSessionAsync();
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };

        var response = await agent.RunAsync(messages, session);
        var resultText = response.Text ?? "(no response)";

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
            });
        }
        else
        {
            _logger.LogWarning("No notification sink available for task {TaskId}", taskId);
        }
    }
}
