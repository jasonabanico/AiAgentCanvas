using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AiAgentCanvas.EvalTests;

/// <summary>
/// Shared setup for the evaluation suite. Cases are checked into source rather
/// than authored at runtime, so a regression shows up as a failing build instead
/// of a number nobody looked at.
/// <para>
/// Every case needs a live model, so the suite skips itself when none is
/// configured. That keeps a plain <c>dotnet test</c> green on a fresh clone while
/// still failing loudly in CI, where the credentials are present.
/// </para>
/// </summary>
public static class EvalHarness
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile("appsettings.local.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    public static string? Endpoint => Value("AIFoundry:Endpoint");
    public static string? ApiKey => Value("AIFoundry:Key");
    public static string Deployment => Value("AIFoundry:DeploymentName") ?? "gpt-4o";

    /// <summary>
    /// Model used to grade. Distinct from the model under test wherever one is
    /// configured, because a model grading its own output scores it generously.
    /// </summary>
    public static string JudgeDeployment => Value("AIFoundry:JudgeDeploymentName") ?? Deployment;

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !Endpoint!.Contains("YOUR-RESOURCE", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Reason surfaced on skipped tests, so a silent skip is not mistaken for a pass.</summary>
    public const string SkipReason =
        "No model configured. Set AIFoundry:Endpoint and AIFoundry:Key, or the matching environment variables.";

    public static IChatClient CreateChatClient(string? deployment = null)
    {
        var client = new AzureOpenAIClient(new Uri(Endpoint!), new AzureKeyCredential(ApiKey!));
        return client.GetChatClient(deployment ?? Deployment).AsIChatClient();
    }

    /// <summary>
    /// Disk-backed reporting writes an HTML report and caches responses between
    /// runs, so re-running the suite locally does not re-bill every case.
    /// </summary>
    public static ReportingConfiguration CreateReporting(IEnumerable<IEvaluator> evaluators, string executionName)
    {
        var storageRoot = Environment.GetEnvironmentVariable("EVAL_STORAGE_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "aiagentcanvas-evals");

        return DiskBasedReportingConfiguration.Create(
            storageRootPath: storageRoot,
            evaluators: evaluators,
            chatConfiguration: new ChatConfiguration(CreateChatClient(JudgeDeployment)),
            enableResponseCaching: true,
            executionName: executionName);
    }

    private static string? Value(string key) =>
        Configuration[key] ?? Configuration[key.Replace(':', '_')];
}

/// <summary>
/// Fact that skips instead of failing when no model is configured. xUnit shows it
/// as skipped with <see cref="EvalHarness.SkipReason"/> attached.
/// </summary>
public sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute()
    {
        if (!EvalHarness.IsConfigured)
            Skip = EvalHarness.SkipReason;
    }
}

/// <summary>Theory that skips instead of failing when no model is configured.</summary>
public sealed class ModelTheoryAttribute : TheoryAttribute
{
    public ModelTheoryAttribute()
    {
        if (!EvalHarness.IsConfigured)
            Skip = EvalHarness.SkipReason;
    }
}
