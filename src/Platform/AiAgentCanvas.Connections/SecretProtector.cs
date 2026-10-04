using Microsoft.AspNetCore.DataProtection;

namespace AiAgentCanvas.Connections;

/// <summary>Encrypts a secret for storage. Implementations must be authenticated, not merely obscured.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <exception cref="System.Security.Cryptography.CryptographicException">The value was tampered with or the key is gone.</exception>
    string Unprotect(string protectedText);
}

/// <summary>
/// Uses ASP.NET Core Data Protection, which encrypts and authenticates with a managed
/// key ring. The key ring location is the security boundary: whoever can read both the
/// database and the keys can read the secrets.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    public const string Purpose = "AiAgentCanvas.Connections.Secrets.v1";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
