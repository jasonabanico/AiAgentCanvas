using System.ComponentModel;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Core.Agents;

public sealed class AgentMailboxToolProvider
{
    private readonly IAgentMailbox _mailbox;
    private readonly IAgentRegistry _registry;
    private readonly ILogger<AgentMailboxToolProvider> _logger;

    public AgentMailboxToolProvider(IAgentMailbox mailbox, IAgentRegistry registry, ILogger<AgentMailboxToolProvider> logger)
    {
        _mailbox = mailbox;
        _registry = registry;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(SendToAgent, "send_to_agent",
                "Send a message to another agent. The message will be queued and processed when the target agent runs next."),
            AIFunctionFactory.Create(CheckInbox, "check_inbox",
                "Check for pending messages from other agents"),
            AIFunctionFactory.Create(ReplyToMessage, "reply_to_message",
                "Reply to a message from another agent"),
        ];
    }

    [Description("Send a message to another agent for asynchronous processing")]
    private string SendToAgent(
        [Description("Name of the agent to send to (use list_available_agents to see options)")] string targetAgent,
        [Description("The message content to send")] string message,
        [Description("Your agent name (the sender)")] string senderAgent)
    {
        var targetInfo = _registry.GetAgentInfo(targetAgent);
        if (targetInfo is null)
            return JsonSerializer.Serialize(new { error = $"Agent '{targetAgent}' not found. Use list_available_agents to see available agents." });

        var messageId = _mailbox.Send(senderAgent, targetAgent, message);
        _logger.LogInformation("Agent {Sender} sent message {Id} to {Recipient}", senderAgent, messageId, targetAgent);

        return JsonSerializer.Serialize(new { status = "sent", messageId, to = targetAgent });
    }

    [Description("Check for pending messages from other agents")]
    private string CheckInbox(
        [Description("Your agent name to check inbox for")] string agentName,
        [Description("Only show pending (unread) messages (default: true)")] bool pendingOnly = true)
    {
        var messages = _mailbox.CheckInbox(agentName, pendingOnly);
        return JsonSerializer.Serialize(new
        {
            count = messages.Count,
            messages = messages.Select(m => new
            {
                m.Id,
                m.Sender,
                m.Message,
                m.Status,
                m.CreatedAt,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Reply to a message from another agent")]
    private string ReplyToMessage(
        [Description("The message ID to reply to")] string messageId,
        [Description("Your reply content")] string response)
    {
        var replied = _mailbox.Reply(messageId, response);
        if (!replied)
            return JsonSerializer.Serialize(new { error = $"Message '{messageId}' not found or already replied to" });

        _logger.LogInformation("Replied to message {Id}", messageId);
        return JsonSerializer.Serialize(new { status = "replied", messageId });
    }
}
