using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace AiAgentCanvas.Authentication.Schemes;

/// <summary>
/// OIDC bearer token validation against any authority. Microsoft Entra ID, Auth0,
/// Okta and Keycloak all work through this one scheme, which is why the platform
/// does not take a dependency on a vendor-specific identity library.
/// <para>
/// Entra ID: set <c>Authority</c> to
/// <c>https://login.microsoftonline.com/{tenant}/v2.0</c> and <c>Audience</c> to
/// the API's application id URI or client id.
/// </para>
/// </summary>
public sealed class JwtBearerScheme : IAgentAuthenticationScheme
{
    public const string SchemeName = "JwtBearer";

    public string Name => SchemeName;

    public void Register(AuthenticationBuilder builder, AgentAuthenticationOptions options)
    {
        var jwt = options.JwtBearer;

        if (string.IsNullOrWhiteSpace(jwt.Authority))
        {
            throw new InvalidOperationException(
                "Authentication:JwtBearer:Authority is required when the JwtBearer scheme is enabled. "
                + "For Entra ID use https://login.microsoftonline.com/{tenant}/v2.0.");
        }

        builder.AddJwtBearer(SchemeName, bearer =>
        {
            bearer.Authority = jwt.Authority;
            bearer.Audience = jwt.Audience;
            bearer.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = !string.IsNullOrWhiteSpace(jwt.Audience),
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // Default is five minutes, which is a long time for a revoked token.
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });
    }

    /// <summary>
    /// Scope requirements are enforced by the authorization policy rather than the
    /// handler, so a token can be valid while still lacking permission.
    /// </summary>
    public static bool HasRequiredScopes(IEnumerable<string> required, string? scopeClaim)
    {
        var requiredList = required.ToList();
        if (requiredList.Count == 0)
            return true;

        if (string.IsNullOrWhiteSpace(scopeClaim))
            return false;

        var granted = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return requiredList.All(scope => granted.Contains(scope, StringComparer.OrdinalIgnoreCase));
    }
}
