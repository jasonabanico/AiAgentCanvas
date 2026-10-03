using System.Globalization;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Data.Sqlite;

namespace AiAgentCanvas.Connections;

/// <summary>
/// Durable storage for connections and their secrets. Metadata is stored in the clear so
/// it can be listed and queried; every secret is encrypted before it reaches the database
/// and decrypted only when a credential is requested.
/// </summary>
public sealed class ConnectionStore
{
    private readonly string _connectionString;
    private readonly ISecretProtector _protector;

    public ConnectionStore(string databasePath, ISecretProtector protector)
    {
        _protector = protector;

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        InitSchema();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void InitSchema()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS connections (
                id TEXT PRIMARY KEY,
                connector_id TEXT NOT NULL,
                label TEXT NOT NULL,
                owner_scope TEXT NOT NULL,
                auth_kind TEXT NOT NULL,
                status TEXT NOT NULL,
                status_detail TEXT,
                scopes TEXT NOT NULL,
                settings TEXT NOT NULL,
                expires_at TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_connections_connector ON connections(connector_id);
            CREATE TABLE IF NOT EXISTS connection_secrets (
                connection_id TEXT PRIMARY KEY,
                payload TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS oauth_apps (
                provider TEXT PRIMARY KEY,
                client_id TEXT NOT NULL,
                client_secret TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void Save(Connection connection)
    {
        connection.UpdatedAt = DateTimeOffset.UtcNow;

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO connections (id, connector_id, label, owner_scope, auth_kind, status, status_detail,
                                     scopes, settings, expires_at, created_at, updated_at)
            VALUES ($id, $connector, $label, $owner, $auth, $status, $detail, $scopes, $settings, $expires, $created, $updated)
            ON CONFLICT(id) DO UPDATE SET
                connector_id = $connector, label = $label, owner_scope = $owner, auth_kind = $auth,
                status = $status, status_detail = $detail, scopes = $scopes, settings = $settings,
                expires_at = $expires, updated_at = $updated
            """;
        cmd.Parameters.AddWithValue("$id", connection.Id);
        cmd.Parameters.AddWithValue("$connector", connection.ConnectorId);
        cmd.Parameters.AddWithValue("$label", connection.Label);
        cmd.Parameters.AddWithValue("$owner", connection.OwnerScope);
        cmd.Parameters.AddWithValue("$auth", connection.Auth.ToString());
        cmd.Parameters.AddWithValue("$status", connection.Status.ToString());
        cmd.Parameters.AddWithValue("$detail", (object?)connection.StatusDetail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$scopes", JsonSerializer.Serialize(connection.Scopes));
        cmd.Parameters.AddWithValue("$settings", JsonSerializer.Serialize(connection.Settings));
        cmd.Parameters.AddWithValue("$expires", connection.ExpiresAt is { } e ? Format(e) : DBNull.Value);
        cmd.Parameters.AddWithValue("$created", Format(connection.CreatedAt));
        cmd.Parameters.AddWithValue("$updated", Format(connection.UpdatedAt));
        cmd.ExecuteNonQuery();
    }

    public Connection? Get(string id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM connections WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadAll(cmd).FirstOrDefault();
    }

    public IReadOnlyList<Connection> List(string? connectorId = null)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM connections {(connectorId is null ? "" : "WHERE connector_id = $connector")} ORDER BY created_at";
        if (connectorId is not null)
            cmd.Parameters.AddWithValue("$connector", connectorId);
        return ReadAll(cmd);
    }

    /// <summary>Removes the connection and its secrets together.</summary>
    public bool Delete(string id)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        using (var secrets = db.CreateCommand())
        {
            secrets.Transaction = tx;
            secrets.CommandText = "DELETE FROM connection_secrets WHERE connection_id = $id";
            secrets.Parameters.AddWithValue("$id", id);
            secrets.ExecuteNonQuery();
        }

        int removed;
        using (var conn = db.CreateCommand())
        {
            conn.Transaction = tx;
            conn.CommandText = "DELETE FROM connections WHERE id = $id";
            conn.Parameters.AddWithValue("$id", id);
            removed = conn.ExecuteNonQuery();
        }

        tx.Commit();
        return removed > 0;
    }

    public void SetStatus(string id, ConnectionStatus status, string? detail)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE connections SET status = $status, status_detail = $detail, updated_at = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        cmd.ExecuteNonQuery();
    }

    public void SaveSecret(string connectionId, SecretPayload payload, DateTimeOffset? expiresAt = null)
    {
        var protectedText = _protector.Protect(JsonSerializer.Serialize(payload));

        using var db = Open();
        using var tx = db.BeginTransaction();

        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO connection_secrets (connection_id, payload, updated_at) VALUES ($id, $payload, $now)
                ON CONFLICT(connection_id) DO UPDATE SET payload = $payload, updated_at = $now
                """;
            cmd.Parameters.AddWithValue("$id", connectionId);
            cmd.Parameters.AddWithValue("$payload", protectedText);
            cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
            cmd.ExecuteNonQuery();
        }

        using (var expiry = db.CreateCommand())
        {
            expiry.Transaction = tx;
            expiry.CommandText = "UPDATE connections SET expires_at = $expires, updated_at = $now WHERE id = $id";
            expiry.Parameters.AddWithValue("$id", connectionId);
            expiry.Parameters.AddWithValue("$expires", expiresAt is { } e ? Format(e) : DBNull.Value);
            expiry.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
            expiry.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public SecretPayload? GetSecret(string connectionId)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM connection_secrets WHERE connection_id = $id";
        cmd.Parameters.AddWithValue("$id", connectionId);

        if (cmd.ExecuteScalar() is not string protectedText)
            return null;

        return JsonSerializer.Deserialize<SecretPayload>(_protector.Unprotect(protectedText));
    }

    public void SaveOAuthApp(OAuthApp app)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO oauth_apps (provider, client_id, client_secret, updated_at) VALUES ($provider, $id, $secret, $now)
            ON CONFLICT(provider) DO UPDATE SET client_id = $id, client_secret = $secret, updated_at = $now
            """;
        cmd.Parameters.AddWithValue("$provider", app.Provider);
        cmd.Parameters.AddWithValue("$id", app.ClientId);
        cmd.Parameters.AddWithValue("$secret", _protector.Protect(app.ClientSecret));
        cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        cmd.ExecuteNonQuery();
    }

    public OAuthApp? GetOAuthApp(string provider)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT client_id, client_secret FROM oauth_apps WHERE provider = $provider";
        cmd.Parameters.AddWithValue("$provider", provider);

        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new OAuthApp(provider, reader.GetString(0), _protector.Unprotect(reader.GetString(1)))
            : null;
    }

    private const string Columns =
        "id, connector_id, label, owner_scope, auth_kind, status, status_detail, scopes, settings, expires_at, created_at, updated_at";

    private static List<Connection> ReadAll(SqliteCommand cmd)
    {
        var results = new List<Connection>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Connection
            {
                Id = reader.GetString(0),
                ConnectorId = reader.GetString(1),
                Label = reader.GetString(2),
                OwnerScope = reader.GetString(3),
                Auth = Enum.Parse<AuthKind>(reader.GetString(4)),
                Status = Enum.Parse<ConnectionStatus>(reader.GetString(5)),
                StatusDetail = reader.IsDBNull(6) ? null : reader.GetString(6),
                Scopes = JsonSerializer.Deserialize<List<string>>(reader.GetString(7)) ?? [],
                Settings = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(8)) ?? [],
                ExpiresAt = reader.IsDBNull(9) ? null : Parse(reader.GetString(9)),
                CreatedAt = Parse(reader.GetString(10)),
                UpdatedAt = Parse(reader.GetString(11)),
            });
        }
        return results;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
