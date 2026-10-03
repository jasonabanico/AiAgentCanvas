namespace AiAgentCanvas.Abstractions;

public enum AuthKind
{
    None,

    /// <summary>One secret string, sent as a bearer token or a header.</summary>
    ApiKey,

    /// <summary>A key id and a secret, sent as basic authentication.</summary>
    KeyPair,

    /// <summary>An OAuth 2.0 access token that expires and is refreshed.</summary>
    OAuth2,
}

/// <summary>
/// What a connector needs to authenticate one call. <see cref="ToString"/> hides the
/// values so a credential that reaches a log line or an exception message does not
/// leak. Equality still compares the values, so do not use it as a lookup key in a
/// place that is logged.
/// </summary>
/// <param name="Kind">How the credential is presented.</param>
/// <param name="Value">The API key, the key id, or the access token.</param>
/// <param name="Secret">The key secret, for <see cref="AuthKind.KeyPair"/>.</param>
/// <param name="Extras">Further secrets a connector needs, such as a webhook signing key.</param>
public sealed record ConnectorCredential(
    AuthKind Kind,
    string Value,
    string? Secret = null,
    IReadOnlyDictionary<string, string>? Extras = null)
{
    public override string ToString() => $"ConnectorCredential({Kind}, [redacted])";
}

public enum CredentialFailure
{
    /// <summary>No connection has that id.</summary>
    NotFound,

    /// <summary>The provider refused the stored credential. A person must connect the account again.</summary>
    NeedsReauth,

    /// <summary>The credential could not be produced now, for example because the provider was unreachable.</summary>
    Unavailable,
}

public sealed class CredentialException : Exception
{
    public CredentialException(CredentialFailure failure, string message, Exception? inner = null)
        : base(message, inner) => Failure = failure;

    public CredentialFailure Failure { get; }
}

/// <summary>
/// The only way a connector obtains a secret. Implementations decrypt, and for OAuth
/// refresh an expiring token, so a caller always receives a credential that works now.
/// </summary>
public interface ICredentialProvider
{
    /// <exception cref="CredentialException">The connection is unknown, needs re-authorization, or could not be refreshed.</exception>
    ValueTask<ConnectorCredential> GetAsync(string connectionId, CancellationToken ct = default);

    /// <summary>Forces a refresh of an OAuth credential, for a caller that just received a 401.</summary>
    ValueTask<ConnectorCredential> RefreshAsync(string connectionId, CancellationToken ct = default);

    Task MarkNeedsReauthAsync(string connectionId, string reason, CancellationToken ct = default);
}
