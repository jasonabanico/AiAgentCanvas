using AiAgentCanvas.Abstractions;
using Microsoft.Data.Sqlite;

namespace AiAgentCanvas.Core.Agents;

public sealed class SqliteAgentMailbox : IAgentMailbox, IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteAgentMailbox(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS agent_messages (
                id TEXT PRIMARY KEY,
                sender TEXT NOT NULL,
                recipient TEXT NOT NULL,
                message TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                response TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                responded_at TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_agent_messages_recipient ON agent_messages(recipient, status);
            """;
        cmd.ExecuteNonQuery();
    }

    public string Send(string sender, string recipient, string message)
    {
        var id = $"msg-{Guid.NewGuid():N}"[..15];
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_messages (id, sender, recipient, message)
            VALUES (@id, @sender, @recipient, @message)
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@sender", sender);
        cmd.Parameters.AddWithValue("@recipient", recipient);
        cmd.Parameters.AddWithValue("@message", message);
        cmd.ExecuteNonQuery();
        return id;
    }

    public List<MailboxMessage> CheckInbox(string recipient, bool pendingOnly = true)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = pendingOnly
            ? "SELECT id, sender, recipient, message, status, response, created_at, responded_at FROM agent_messages WHERE recipient = @recipient AND status = 'pending' ORDER BY created_at ASC"
            : "SELECT id, sender, recipient, message, status, response, created_at, responded_at FROM agent_messages WHERE recipient = @recipient ORDER BY created_at DESC LIMIT 50";
        cmd.Parameters.AddWithValue("@recipient", recipient);

        var messages = new List<MailboxMessage>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            messages.Add(ReadMessage(reader));
        return messages;
    }

    public bool Reply(string messageId, string response)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE agent_messages
            SET status = 'replied', response = @response, responded_at = datetime('now')
            WHERE id = @id AND status = 'pending'
            """;
        cmd.Parameters.AddWithValue("@id", messageId);
        cmd.Parameters.AddWithValue("@response", response);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool MarkRead(string messageId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE agent_messages
            SET status = 'read'
            WHERE id = @id AND status = 'pending'
            """;
        cmd.Parameters.AddWithValue("@id", messageId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public MailboxMessage? GetMessage(string messageId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, sender, recipient, message, status, response, created_at, responded_at FROM agent_messages WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", messageId);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadMessage(reader) : null;
    }

    public int PendingCount(string recipient)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_messages WHERE recipient = @recipient AND status = 'pending'";
        cmd.Parameters.AddWithValue("@recipient", recipient);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static MailboxMessage ReadMessage(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Sender = reader.GetString(1),
        Recipient = reader.GetString(2),
        Message = reader.GetString(3),
        Status = reader.GetString(4),
        Response = reader.IsDBNull(5) ? null : reader.GetString(5),
        CreatedAt = reader.GetString(6),
        RespondedAt = reader.IsDBNull(7) ? null : reader.GetString(7),
    };

    public void Dispose() => _connection.Dispose();
}
