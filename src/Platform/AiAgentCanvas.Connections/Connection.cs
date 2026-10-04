using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.Connections;

public enum ConnectionStatus
{
    Connected,

    /// <summary>The provider refused the credential. The account must be connected again.</summary>
    NeedsReauth,

    Error,
}

/// <summary>
/// One configured account for one connector, such as a Gmail mailbox or a Twilio
/// account. Holds no secret: those live encrypted in a separate table and are read only
/// through <see cref="ICredentialProvider"/>.
/// </summary>
public sealed class Connection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>The connector this account belongs to, such as <c>twilio-sms</c> or <c>gmail</c>.</summary>
    public string ConnectorId { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Who may use the connection. <c>deployment</c> is shared by the whole host. The
    /// field exists so per-user or per-agent scopes can be added without a migration.
    /// </summary>
    public string OwnerScope { get; set; } = "deployment";

    public AuthKind Auth { get; set; }

    public ConnectionStatus Status { get; set; } = ConnectionStatus.Connected;

    public string? StatusDetail { get; set; }

    public List<string> Scopes { get; set; } = [];

    /// <summary>Non-secret configuration: a sender number, an account id, a base URL.</summary>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>When the access token expires. Null for credentials that do not expire.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>The decrypted secrets of one connection. Never log or serialize this.</summary>
public sealed class SecretPayload
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? ApiKey { get; set; }
    public string? KeyId { get; set; }
    public string? KeySecret { get; set; }
    public Dictionary<string, string> Extras { get; set; } = [];

    public override string ToString() => "SecretPayload([redacted])";
}

public sealed record OAuthApp(string Provider, string ClientId, string ClientSecret)
{
    public override string ToString() => $"OAuthApp({Provider}, [redacted])";
}
