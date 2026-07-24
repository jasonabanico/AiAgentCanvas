#pragma warning disable MEAI001

using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Http;

namespace AiAgentCanvas.Orchestration.Services;

internal sealed class HttpContextSessionIsolationKeyProvider : SessionIsolationKeyProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextSessionIsolationKeyProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override ValueTask<string?> GetSessionIsolationKeyAsync(CancellationToken cancellationToken = default)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return new ValueTask<string?>((string?)null);

        var identity = context.User.Identity;
        if (identity is { IsAuthenticated: true, Name: { } name })
            return new ValueTask<string?>(name);

        var ip = context.Connection.RemoteIpAddress?.ToString();
        return new ValueTask<string?>(ip);
    }
}
