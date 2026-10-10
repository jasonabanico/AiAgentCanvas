using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.StructuredOutput;

/// <summary>
/// Asks a model for JSON in a given shape and does not trust the first answer. The schema is
/// sent two ways: as the provider's native response format, and in the instruction text,
/// because a provider that ignores the format still follows the text. Each answer is parsed
/// and checked against the schema, and one that fails goes back to the model with the
/// specific errors. This is the difference between a demo that works on a good day and an
/// extraction a program can depend on.
/// </summary>
public sealed class StructuredResponder : IStructuredResponder
{
    private readonly IChatClient _client;
    private readonly StructuredOptions _options;
    private readonly ILogger<StructuredResponder> _logger;

    public StructuredResponder(IChatClient client, StructuredOptions options, ILogger<StructuredResponder> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<StructuredResult> RespondAsync(StructuredRequest request, CancellationToken ct = default)
    {
        if (request.Schema.ValueKind is not (JsonValueKind.Object or JsonValueKind.True))
            return Fail("The schema must be a JSON object.", 0, null);

        var schemaText = request.Schema.GetRawText();
        if (schemaText.Length > _options.MaxSchemaChars)
            return Fail($"The schema is {schemaText.Length} characters. The limit is {_options.MaxSchemaChars}.", 0, null);

        if (request.Input is { } input && input.Length > _options.MaxInputChars)
            return Fail($"The input is {input.Length} characters. The limit is {_options.MaxInputChars}.", 0, null);

        var attempts = Math.Clamp(request.MaxAttempts, 1, Math.Max(1, _options.MaxAttemptsCap));

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                $"{request.Instruction}\n\nAnswer with one JSON value that matches this JSON Schema, and nothing else. "
                + $"No commentary and no code fences.\n\nSchema:\n{schemaText}"),
            BuildUserMessage(request),
        };

        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                request.Schema, SafeName(request.SchemaName), request.SchemaDescription),
        };

        List<string> errors = [];
        string? raw = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var response = await _client.GetResponseAsync(messages, options, ct);
            raw = response.Text ?? string.Empty;

            if (TryParse(raw, out var value, out var parseError))
            {
                var problems = JsonSchemaValidator.Validate(request.Schema, value);
                if (problems.Count == 0)
                    return new StructuredResult(true, value, [], attempt, raw);

                errors = problems.ToList();
            }
            else
            {
                errors = [parseError];
            }

            _logger.LogInformation("Structured answer rejected on attempt {Attempt} of {Max}: {Errors}",
                attempt, attempts, string.Join("; ", errors.Take(3)));

            if (attempt == attempts)
                break;

            messages.Add(new ChatMessage(ChatRole.Assistant, raw));
            messages.Add(new ChatMessage(ChatRole.User,
                "That answer was rejected:\n- " + string.Join("\n- ", errors.Take(10))
                + "\nReply again with corrected JSON only."));
        }

        return new StructuredResult(false, null, errors, attempts, raw);
    }

    private static ChatMessage BuildUserMessage(StructuredRequest request)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(request.Input))
            contents.Add(new TextContent(request.Input));

        foreach (var image in request.Images ?? [])
            contents.Add(new DataContent(image.Data, image.MediaType));

        if (contents.Count == 0)
            contents.Add(new TextContent("Follow the instruction."));

        return new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>Models often wrap JSON in a code fence or add a sentence around it. Accept both.</summary>
    internal static bool TryParse(string text, out JsonElement value, out string error)
    {
        value = default;
        var candidate = text.Trim();

        if (candidate.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = candidate.IndexOf('\n');
            var closing = candidate.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && closing > firstNewline)
                candidate = candidate[(firstNewline + 1)..closing].Trim();
        }

        if (candidate.Length == 0)
        {
            error = "The answer was empty. Reply with JSON.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(candidate);
            value = document.RootElement.Clone();
            error = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            // Fall through to look for a JSON value inside surrounding text.
        }

        var start = candidate.IndexOfAny(['{', '[']);
        var end = candidate.LastIndexOfAny(['}', ']']);
        if (start >= 0 && end > start)
        {
            try
            {
                using var document = JsonDocument.Parse(candidate[start..(end + 1)]);
                value = document.RootElement.Clone();
                error = string.Empty;
                return true;
            }
            catch (JsonException)
            {
            }
        }

        error = "The answer was not valid JSON. Reply with a single JSON value and no other text.";
        return false;
    }

    private static string SafeName(string name)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
        return cleaned.Length == 0 ? "result" : cleaned[..Math.Min(cleaned.Length, 64)];
    }

    private static StructuredResult Fail(string message, int attempts, string? raw) =>
        new(false, null, [message], attempts, raw);
}
