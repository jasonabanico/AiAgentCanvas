#pragma warning disable MEAI001

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.EventTriggers;

public static class EventTriggerToolProvider
{
    public static IReadOnlyList<AITool> CreateTools(TriggerRegistry registry)
    {
        return
        [
            AIFunctionFactory.Create(
                [Description("Create a new event trigger that fires on a schedule, file change, or webhook")]
                (string name, string type, string agentMessage, string? cronExpression, string? watchPath, string? targetAgent) =>
                {
                    if (!Enum.TryParse<EventTriggerType>(type, true, out var triggerType))
                        return JsonSerializer.Serialize(new { error = $"Invalid type: {type}. Use Scheduled, FileWatch, or Webhook." });

                    var trigger = new EventTrigger
                    {
                        Name = name,
                        Type = triggerType,
                        CronExpression = cronExpression,
                        WatchPath = watchPath,
                        AgentMessage = agentMessage,
                        TargetAgent = targetAgent,
                    };
                    registry.Register(trigger);
                    return JsonSerializer.Serialize(new { created = true, trigger.Id, trigger.Name, Type = trigger.Type.ToString() });
                }, "create_trigger"),

            AIFunctionFactory.Create(
                [Description("List all registered event triggers")]
                () =>
                {
                    var triggers = registry.ListAll();
                    return JsonSerializer.Serialize(triggers.Select(t => new
                    {
                        t.Id, t.Name, Type = t.Type.ToString(), t.Enabled,
                        t.AgentMessage, t.FireCount, LastFired = t.LastFired?.ToString("g"),
                    }));
                }, "list_triggers"),

            AIFunctionFactory.Create(
                [Description("Enable or disable an event trigger")]
                (string triggerId, bool enabled) =>
                {
                    var trigger = registry.Get(triggerId);
                    if (trigger is null)
                        return JsonSerializer.Serialize(new { error = "Trigger not found" });
                    trigger.Enabled = enabled;
                    return JsonSerializer.Serialize(new { updated = true, trigger.Id, trigger.Enabled });
                }, "toggle_trigger"),

            AIFunctionFactory.Create(
                [Description("Remove an event trigger")]
                (string triggerId) =>
                {
                    var removed = registry.Remove(triggerId);
                    return JsonSerializer.Serialize(new { removed, triggerId });
                }, "remove_trigger"),
        ];
    }
}
