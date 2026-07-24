using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration.Services;

public sealed class AIHealthCheck : IHostedService, IHealthCheck
{
    private readonly IChatClient _chatClient;
    private readonly AIAgent _agent;
    private readonly ILogger<AIHealthCheck> _logger;

    public AIHealthCheck(IChatClient chatClient, AIAgent agent, ILogger<AIHealthCheck> logger)
    {
        _chatClient = chatClient;
        _agent = agent;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AI health check: verifying AI endpoint...");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await _chatClient.GetResponseAsync(
                [new(ChatRole.User, "ping")],
                new ChatOptions { MaxOutputTokens = 5, Temperature = 0 },
                cts.Token);

            _logger.LogInformation("AI health check passed. Model responded: {Response}",
                response.Text?[..Math.Min(response.Text.Length, 50)]);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("AI health check FAILED: AI endpoint did not respond within 15 seconds. Check your AI provider configuration in appsettings.json.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI health check FAILED: {Message}. Check your AI provider configuration in appsettings.json.", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        var sw = Stopwatch.StartNew();

        // Check 1: AI connectivity (bare chat client, no tools)
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await _chatClient.GetResponseAsync(
                [new(ChatRole.User, "ping")],
                new ChatOptions { MaxOutputTokens = 5, Temperature = 0 },
                cts.Token);

            data["ai_connectivity"] = "pass";
            data["ai_response_ms"] = sw.ElapsedMilliseconds;
            _logger.LogInformation("Health: AI connectivity passed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            data["ai_connectivity"] = "fail";
            data["ai_error"] = ex.Message;
            _logger.LogError(ex, "Health: AI connectivity failed");
            return HealthCheckResult.Unhealthy("AI endpoint is not responding.", ex, data);
        }

        // Check 2: Full agent pipeline (planning middleware, context providers, streaming)
        try
        {
            sw.Restart();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var session = await _agent.CreateSessionAsync(cts.Token);
            var messages = new List<ChatMessage> { new(ChatRole.User, "Reply with only the word OK.") };
            var gotOutput = false;

            await foreach (var update in _agent.RunStreamingAsync(messages, session, cancellationToken: cts.Token))
            {
                if (update.Text is { Length: > 0 })
                    gotOutput = true;
            }

            data["agent_pipeline"] = gotOutput ? "pass" : "no_output";
            data["agent_pipeline_ms"] = sw.ElapsedMilliseconds;
            _logger.LogInformation("Health: Agent pipeline passed in {ElapsedMs}ms", sw.ElapsedMilliseconds);

            if (!gotOutput)
                return HealthCheckResult.Degraded("Agent pipeline produced no output.", data: data);
        }
        catch (OperationCanceledException)
        {
            data["agent_pipeline"] = "timeout";
            data["agent_pipeline_ms"] = sw.ElapsedMilliseconds;
            _logger.LogError("Health: Agent pipeline timed out after 30s");
            return HealthCheckResult.Unhealthy("Agent pipeline timed out after 30 seconds. The model may be overloaded or the tool count may be too high.", data: data);
        }
        catch (Exception ex)
        {
            data["agent_pipeline"] = "fail";
            data["agent_error"] = ex.Message;
            _logger.LogError(ex, "Health: Agent pipeline failed");
            return HealthCheckResult.Unhealthy("Agent pipeline failed.", ex, data);
        }

        data["total_ms"] = sw.ElapsedMilliseconds;
        return HealthCheckResult.Healthy("All checks passed.", data);
    }
}
