using Microsoft.Agents.AI;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace VectorStore.Sqlite;

public sealed class SqliteChatHistoryProvider : ChatHistoryProvider
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<SqliteChatHistoryProvider> _logger;
    private bool _initialized;

    public SqliteChatHistoryProvider(string connectionString, ILogger<SqliteChatHistoryProvider> logger)
    {
        _connection = new SqliteConnection(connectionString);
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync(ct);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS chat_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                conversation_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                timestamp TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS idx_chat_history_conversation
                ON chat_history(conversation_id, id);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
        _initialized = true;
    }

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var conversationId = GetConversationId(context.Session);
        if (string.IsNullOrEmpty(conversationId))
            return [];

        var messages = new List<ChatMessage>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT role, content FROM chat_history WHERE conversation_id = @cid ORDER BY id";
        cmd.Parameters.AddWithValue("@cid", conversationId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var role = reader.GetString(0) switch
            {
                "system" => ChatRole.System,
                "assistant" => ChatRole.Assistant,
                _ => ChatRole.User,
            };
            messages.Add(new ChatMessage(role, reader.GetString(1)));
        }

        _logger.LogDebug("Loaded {Count} messages for conversation {ConversationId}", messages.Count, conversationId);
        return messages;
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var conversationId = GetConversationId(context.Session);
        if (string.IsNullOrEmpty(conversationId))
            return;

        var messagesToStore = new List<ChatMessage>();
        if (context.RequestMessages is not null)
            messagesToStore.AddRange(context.RequestMessages);
        if (context.ResponseMessages is not null)
            messagesToStore.AddRange(context.ResponseMessages);

        foreach (var msg in messagesToStore)
        {
            var text = msg.Text;
            if (string.IsNullOrEmpty(text)) continue;

            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO chat_history (conversation_id, role, content)
                VALUES (@cid, @role, @content)
                """;
            cmd.Parameters.AddWithValue("@cid", conversationId);
            cmd.Parameters.AddWithValue("@role", msg.Role.Value);
            cmd.Parameters.AddWithValue("@content", text);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogDebug("Stored {Count} messages for conversation {ConversationId}", messagesToStore.Count, conversationId);
    }

    private static string? GetConversationId(AgentSession? session)
    {
        if (session is null) return null;
        return session.StateBag.TryGetValue<string>("conversationId", out var id) ? id : null;
    }
}
