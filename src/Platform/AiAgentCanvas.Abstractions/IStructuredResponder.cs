using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Abstractions;

/// <summary>An image handed to a model. <see cref="MediaType"/> is a type such as <c>image/png</c>.</summary>
public sealed record ImageInput(ReadOnlyMemory<byte> Data, string MediaType);

/// <summary>
/// Asks for an answer in a fixed shape. <see cref="Schema"/> is a JSON Schema document.
/// <see cref="Input"/> is the material to read, and <see cref="Instruction"/> says what to do
/// with it. Images, when given, travel with the input to a model that can see them.
/// </summary>
public sealed record StructuredRequest(
    string Instruction,
    string? Input,
    JsonElement Schema,
    string SchemaName = "result",
    string? SchemaDescription = null,
    IReadOnlyList<ImageInput>? Images = null,
    int MaxAttempts = 3);

/// <summary>
/// The outcome of a structured request. On failure <see cref="Errors"/> says what the last
/// answer got wrong, and <see cref="RawText"/> holds that answer for inspection.
/// </summary>
public sealed record StructuredResult(
    bool Success,
    JsonElement? Value,
    IReadOnlyList<string> Errors,
    int Attempts,
    string? RawText);

public sealed record StructuredResult<T>(bool Success, T? Value, IReadOnlyList<string> Errors, int Attempts);

/// <summary>
/// Gets a model to answer in a shape a program can rely on. The model is asked for JSON that
/// matches a schema, the answer is checked against the schema, and an answer that fails is
/// returned to the model with the errors until it passes or the attempts run out.
/// </summary>
public interface IStructuredResponder
{
    Task<StructuredResult> RespondAsync(StructuredRequest request, CancellationToken ct = default);
}

/// <summary>Named schemas an operator or an agent can refer to without pasting the schema each time.</summary>
public interface ISchemaCatalog
{
    IReadOnlyList<string> Names { get; }

    bool TryGet(string name, out JsonElement schema, out string? description);
}

public static class StructuredResponderExtensions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Asks for an answer shaped like <typeparamref name="T"/>. The schema comes from the
    /// type, so the shape lives in one place: the class.
    /// </summary>
    public static async Task<StructuredResult<T>> RespondAsync<T>(
        this IStructuredResponder responder,
        string instruction,
        string? input = null,
        IReadOnlyList<ImageInput>? images = null,
        int maxAttempts = 3,
        CancellationToken ct = default)
    {
        var schema = AIJsonUtilities.CreateJsonSchema(typeof(T), serializerOptions: Options);
        var result = await responder.RespondAsync(
            new StructuredRequest(instruction, input, schema, typeof(T).Name, null, images, maxAttempts), ct);

        if (!result.Success || result.Value is not { } value)
            return new StructuredResult<T>(false, default, result.Errors, result.Attempts);

        try
        {
            var typed = value.Deserialize<T>(Options);
            return new StructuredResult<T>(typed is not null, typed, [], result.Attempts);
        }
        catch (JsonException ex)
        {
            return new StructuredResult<T>(false, default, [$"The answer matched the schema but could not be read as {typeof(T).Name}: {ex.Message}"], result.Attempts);
        }
    }
}
