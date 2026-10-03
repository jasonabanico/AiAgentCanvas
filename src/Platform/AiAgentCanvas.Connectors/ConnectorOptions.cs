namespace AiAgentCanvas.Connectors;

public enum ConnectorApprovalMode
{
    /// <summary>Tools that send or delete wait for a person to approve each call.</summary>
    Require,

    /// <summary>
    /// Tools run without approval. Calls are still traced and counted. Use it for a
    /// deployment where no person is present to approve and the agent is trusted.
    /// </summary>
    Audit,
}

public sealed class ConnectorOptions
{
    public const string SectionName = "Connectors";

    /// <summary>How often the host compares the stored connections with the running ones.</summary>
    public int ReconcileSeconds { get; set; } = 15;

    /// <summary>How often each connection is asked whether it still works.</summary>
    public int CheckIntervalSeconds { get; set; } = 300;

    /// <summary>How often event-source connectors are polled. Push-only connectors return nothing.</summary>
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>Longest a connector may spend starting, checking or polling before the host gives up.</summary>
    public int OperationTimeoutSeconds { get; set; } = 60;

    public ConnectorApprovalMode ApprovalMode { get; set; } = ConnectorApprovalMode.Require;

    public int MaxWebhookBodyBytes { get; set; } = 262144;

    public int HttpTimeoutSeconds { get; set; } = 60;
}
