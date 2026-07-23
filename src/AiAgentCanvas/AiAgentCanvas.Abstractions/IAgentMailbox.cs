namespace AiAgentCanvas.Abstractions;

public sealed class MailboxMessage
{
    public string Id { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? Response { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? RespondedAt { get; set; }
}

public interface IAgentMailbox
{
    string Send(string sender, string recipient, string message);
    List<MailboxMessage> CheckInbox(string recipient, bool pendingOnly = true);
    bool Reply(string messageId, string response);
    bool MarkRead(string messageId);
    MailboxMessage? GetMessage(string messageId);
    int PendingCount(string recipient);
}
