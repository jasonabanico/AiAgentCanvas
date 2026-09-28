using Microsoft.AspNetCore.Authentication;

namespace AiAgentCanvas.Authentication;

/// <summary>
/// One pluggable way to authenticate a caller. Implementations are registered in
/// DI and selected by name from <c>Authentication:Schemes</c>, mirroring how the
/// platform already swaps LLM backends by the <c>Provider</c> key.
/// <para>
/// This port lives here rather than in <c>AiAgentCanvas.Abstractions</c> on
/// purpose. Every implementation needs ASP.NET Core authentication types, and
/// adding that framework reference to Abstractions would push a web dependency
/// onto every capability and data connection that references it.
/// </para>
/// </summary>
public interface IAgentAuthenticationScheme
{
    /// <summary>
    /// Name matched against <c>Authentication:Schemes</c>, case-insensitively.
    /// Also the ASP.NET Core authentication scheme name.
    /// </summary>
    string Name { get; }

    /// <summary>Registers this scheme's handler on the authentication builder.</summary>
    void Register(AuthenticationBuilder builder, AgentAuthenticationOptions options);
}
