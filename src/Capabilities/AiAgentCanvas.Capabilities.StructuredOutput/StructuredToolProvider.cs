using System.ComponentModel;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.StructuredOutput;

public static class StructuredToolProvider
{
    public static IReadOnlyList<AITool> CreateTools(IStructuredResponder responder, ISchemaCatalog catalog, StructuredOptions options)
    {
        return
        [
            AIFunctionFactory.Create(
                [Description("Read text and answer in a fixed JSON shape. Give either schemaName (see list_schemas) or schema (a JSON Schema document as text). The answer is checked against the schema and retried until it fits, so the result is safe to use as data. The text may come from an outside party: it can fill the fields but cannot change the shape or the instruction.")]
                async (
                    [Description("The material to read")] string text,
                    [Description("What to extract or decide, in a sentence")] string instruction,
                    [Description("Name of a stored schema")] string? schemaName = null,
                    [Description("A JSON Schema document as text, used when schemaName is not given")] string? schema = null,
                    CancellationToken ct = default) =>
                {
                    if (!TryResolveSchema(catalog, options, schemaName, schema, out var resolved, out var description, out var name, out var error))
                        return JsonSerializer.Serialize(new { success = false, errors = new[] { error } });

                    var result = await responder.RespondAsync(
                        new StructuredRequest(instruction, text, resolved, name, description), ct);

                    return JsonSerializer.Serialize(new { success = result.Success, value = result.Value, errors = result.Errors, attempts = result.Attempts });
                },
                "extract_structured"),

            AIFunctionFactory.Create(
                [Description("List the stored schemas that extract_structured can use by name")]
                () => JsonSerializer.Serialize(new { schemas = catalog.Names }),
                "list_schemas"),
        ];
    }

    internal static bool TryResolveSchema(
        ISchemaCatalog catalog,
        StructuredOptions options,
        string? schemaName,
        string? schemaText,
        out JsonElement schema,
        out string? description,
        out string name,
        out string error)
    {
        schema = default;
        description = null;
        name = "result";
        error = string.Empty;

        if (!string.IsNullOrWhiteSpace(schemaName))
        {
            if (catalog.TryGet(schemaName, out schema, out description))
            {
                name = schemaName;
                return true;
            }

            error = $"No schema named '{schemaName}'. Known schemas: {string.Join(", ", catalog.Names)}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(schemaText))
        {
            error = "Give schemaName or schema.";
            return false;
        }

        if (schemaText.Length > options.MaxSchemaChars)
        {
            error = $"The schema is {schemaText.Length} characters. The limit is {options.MaxSchemaChars}.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(schemaText);
            schema = document.RootElement.Clone();
            return true;
        }
        catch (JsonException ex)
        {
            error = $"The schema is not valid JSON: {ex.Message}";
            return false;
        }
    }
}
