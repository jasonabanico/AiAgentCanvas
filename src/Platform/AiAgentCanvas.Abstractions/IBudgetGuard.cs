namespace AiAgentCanvas.Abstractions;

/// <summary>The answer to "may this unattended run start now".</summary>
/// <param name="Allowed">False when a limit has been reached.</param>
/// <param name="Scope">Which limit decided: trigger, agent or total.</param>
/// <param name="Spent">Spend counted against that limit over the window.</param>
/// <param name="Limit">The limit that was reached or approached.</param>
/// <param name="Reason">A sentence an operator can read, set when the run is refused.</param>
/// <param name="RetryAfter">How long to wait before asking again, set when the run is refused.</param>
public sealed record BudgetDecision(
    bool Allowed,
    string? Scope = null,
    double Spent = 0,
    double Limit = 0,
    string? Reason = null,
    TimeSpan? RetryAfter = null)
{
    public static BudgetDecision Allow { get; } = new(true);
}

/// <summary>
/// Spend limits for unattended runs. Interactive chat is not covered, because a person
/// is present to stop it; scheduled tasks, triggers and delegated work are, because
/// nobody is watching them spend.
/// </summary>
public interface IBudgetGuard
{
    Task<BudgetDecision> CheckAsync(string agentName, string? triggerId, CancellationToken ct = default);
}
