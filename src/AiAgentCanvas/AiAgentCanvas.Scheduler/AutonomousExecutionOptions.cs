namespace AiAgentCanvas.Scheduler;

public sealed class AutonomousExecutionOptions
{
    public int MaxIterationsPerRun { get; set; } = 5;
    public int PollIntervalSeconds { get; set; } = 30;
    public bool Enabled { get; set; } = false;
    public string CronExpression { get; set; } = "*/30 * * * * *";
}
