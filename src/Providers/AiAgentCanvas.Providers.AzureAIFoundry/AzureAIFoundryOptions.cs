namespace AiAgentCanvas.Providers.AzureAIFoundry;

public sealed class AzureAIFoundryOptions
{
    public const string SectionName = "AIFoundry";

    public required string Endpoint { get; set; }
    public string? Key { get; set; }
    public required string DeploymentName { get; set; }
    public string? EmbeddingDeploymentName { get; set; }
    public bool UseAzureCredential { get; set; }

    /// <summary>
    /// Optional cheaper deployment. When set, the cost-aware router sends
    /// low-complexity turns here and history summarization uses it too.
    /// </summary>
    public string? EconomyDeploymentName { get; set; }

    /// <summary>
    /// Optional deployment used to grade evaluation output. Point it at a different
    /// model from the one under test: a model grading itself is lenient with its own work.
    /// </summary>
    public string? JudgeDeploymentName { get; set; }
}
