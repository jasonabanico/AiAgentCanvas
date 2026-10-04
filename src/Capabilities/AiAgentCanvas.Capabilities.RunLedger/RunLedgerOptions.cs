namespace AiAgentCanvas.Capabilities.RunLedger;

public sealed class RunLedgerOptions
{
    public const string SectionName = "Agent:RunLedger";

    public string DatabasePath { get; set; } = "run-ledger.db";

    /// <summary>Finished runs older than this are deleted. Zero keeps them indefinitely.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Input, output and error text is cut to this length before it is stored.</summary>
    public int MaxTextChars { get; set; } = 4000;
}
