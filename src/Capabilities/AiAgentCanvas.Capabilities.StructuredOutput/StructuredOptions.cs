namespace AiAgentCanvas.Capabilities.StructuredOutput;

public sealed class StructuredOptions
{
    public const string SectionName = "Agent:Structured";

    /// <summary>Folder of <c>*.schema.json</c> files. Each file name, without the suffix, is a schema name.</summary>
    public string SchemaDirectory { get; set; } = "schemas";

    /// <summary>The most attempts any one request may use, whatever it asks for.</summary>
    public int MaxAttemptsCap { get; set; } = 5;

    /// <summary>Longest input text accepted, so one call cannot spend a whole context window.</summary>
    public int MaxInputChars { get; set; } = 100_000;

    /// <summary>Longest schema accepted from a caller, in characters.</summary>
    public int MaxSchemaChars { get; set; } = 20_000;
}
