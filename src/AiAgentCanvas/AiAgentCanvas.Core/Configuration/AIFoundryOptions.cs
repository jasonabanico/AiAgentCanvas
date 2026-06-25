namespace AiAgentCanvas.Core.Configuration;

public sealed class AIFoundryOptions
{
    public const string SectionName = "AIFoundry";

    public required string Endpoint { get; set; }
    public string? Key { get; set; }
    public required string DeploymentName { get; set; }
    public string? EmbeddingDeploymentName { get; set; }
    public bool UseAzureCredential { get; set; }
}
