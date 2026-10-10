using System.Text.Json;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.StructuredOutput;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

/// <summary>Answers each call from a list and keeps what it was sent.</summary>
public sealed class ScriptedChatClient(params string[] replies) : IChatClient
{
    private int _next;

    public List<List<ChatMessage>> Calls { get; } = [];
    public List<ChatOptions?> Options { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(messages.ToList());
        Options.Add(options);
        var reply = replies[Math.Min(_next++, replies.Length - 1)];
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

public class JsonSchemaValidatorTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IReadOnlyList<string> Check(string schema, string value) =>
        JsonSchemaValidator.Validate(J(schema), J(value));

    private const string Person = """
        {"type":"object","properties":{"name":{"type":"string","minLength":1},"age":{"type":"integer","minimum":0,"maximum":150},
         "role":{"enum":["admin","user"]}},"required":["name","age"],"additionalProperties":false}
        """;

    [Fact]
    public void A_value_that_fits_the_schema_has_no_errors() =>
        Assert.Empty(Check(Person, """{"name":"Ada","age":36,"role":"admin"}"""));

    [Fact]
    public void A_missing_required_property_is_named()
    {
        var errors = Check(Person, """{"name":"Ada"}""");

        Assert.Contains(errors, e => e.Contains("'age'") && e.Contains("missing"));
    }

    [Fact]
    public void A_wrong_type_says_what_was_expected_and_what_was_found()
    {
        var errors = Check(Person, """{"name":"Ada","age":"thirty"}""");

        Assert.Contains(errors, e => e.StartsWith("$.age") && e.Contains("integer") && e.Contains("string"));
    }

    [Theory]
    [InlineData("36.5", false)]
    [InlineData("36.0", true)]
    [InlineData("36", true)]
    public void An_integer_must_be_whole(string age, bool valid) =>
        Assert.Equal(valid, Check(Person, $$"""{"name":"Ada","age":{{age}}}""").Count == 0);

    [Fact]
    public void Numeric_and_string_limits_are_enforced()
    {
        Assert.Contains(Check(Person, """{"name":"Ada","age":200}"""), e => e.Contains("at most 150"));
        Assert.Contains(Check(Person, """{"name":"","age":3}"""), e => e.Contains("at least 1 character"));
    }

    [Fact]
    public void An_enum_lists_the_allowed_values()
    {
        var errors = Check(Person, """{"name":"Ada","age":3,"role":"root"}""");

        Assert.Contains(errors, e => e.Contains("\"admin\"") && e.Contains("\"user\""));
    }

    [Fact]
    public void An_extra_property_is_refused_when_the_schema_says_so()
    {
        var errors = Check(Person, """{"name":"Ada","age":3,"nickname":"A"}""");

        Assert.Contains(errors, e => e.Contains("'nickname'") && e.Contains("not allowed"));
    }

    [Fact]
    public void Array_items_are_checked_with_their_index_in_the_path()
    {
        var errors = Check("""{"type":"array","items":{"type":"integer"},"minItems":1}""", "[1,\"x\",3]");

        Assert.Contains(errors, e => e.StartsWith("$[1]"));
    }

    [Fact]
    public void An_empty_array_breaks_min_items() =>
        Assert.Contains(Check("""{"type":"array","minItems":1}""", "[]"), e => e.Contains("at least 1"));

    [Fact]
    public void A_local_reference_is_followed()
    {
        const string schema = """
            {"$defs":{"name":{"type":"string"}},"type":"object","properties":{"first":{"$ref":"#/$defs/name"}}}
            """;

        Assert.Empty(Check(schema, """{"first":"Ada"}"""));
        Assert.NotEmpty(Check(schema, """{"first":4}"""));
    }

    [Fact]
    public void A_reference_to_nothing_is_reported_and_not_ignored() =>
        Assert.Contains(Check("""{"$ref":"#/$defs/missing"}""", "1"), e => e.Contains("does not define"));

    [Fact]
    public void A_list_of_types_accepts_any_of_them_including_null()
    {
        const string schema = """{"type":["string","null"]}""";

        Assert.Empty(Check(schema, "null"));
        Assert.Empty(Check(schema, "\"x\""));
        Assert.NotEmpty(Check(schema, "3"));
    }

    [Fact]
    public void Any_of_needs_one_match_and_one_of_needs_exactly_one()
    {
        const string any = """{"anyOf":[{"type":"string"},{"type":"integer"}]}""";
        const string one = """{"oneOf":[{"type":"integer"},{"type":"number"}]}""";

        Assert.Empty(Check(any, "3"));
        Assert.NotEmpty(Check(any, "true"));
        Assert.NotEmpty(Check(one, "3"));
        Assert.Empty(Check(one, "3.5"));
    }

    [Fact]
    public void A_pattern_is_applied_and_a_broken_pattern_is_reported()
    {
        Assert.Empty(Check("""{"type":"string","pattern":"^[A-Z]{3}$"}""", "\"ABC\""));
        Assert.NotEmpty(Check("""{"type":"string","pattern":"^[A-Z]{3}$"}""", "\"abc\""));
        Assert.Contains(Check("""{"type":"string","pattern":"("}""", "\"x\""), e => e.Contains("not a valid expression"));
    }

    [Fact]
    public void Unknown_keywords_are_ignored_and_do_not_fail_a_value() =>
        Assert.Empty(Check("""{"type":"string","format":"email","x-note":"anything"}""", "\"not an email\""));

    [Fact]
    public void Errors_are_capped_so_a_bad_array_does_not_flood_the_retry_message()
    {
        var value = "[" + string.Join(",", Enumerable.Repeat("\"x\"", 200)) + "]";

        var errors = Check("""{"type":"array","items":{"type":"integer"}}""", value);

        Assert.InRange(errors.Count, 1, 20);
    }

    [Fact]
    public void A_self_referencing_schema_stops_instead_of_looping()
    {
        const string schema = """{"$defs":{"a":{"$ref":"#/$defs/a"}},"$ref":"#/$defs/a"}""";

        var errors = Check(schema, "1");

        Assert.Contains(errors, e => e.Contains("nests deeper"));
    }
}

public class StructuredResponderTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"city":{"type":"string"},"temp":{"type":"number"}},"required":["city","temp"]}""").RootElement.Clone();

    private static StructuredResponder Responder(ScriptedChatClient client, Action<StructuredOptions>? configure = null)
    {
        var options = new StructuredOptions();
        configure?.Invoke(options);
        return new StructuredResponder(client, options, NullLogger<StructuredResponder>.Instance);
    }

    private static StructuredRequest Request(int attempts = 3, string? input = "It is 21 degrees in Sydney.") =>
        new("Extract the weather.", input, Schema, "weather", null, null, attempts);

    [Fact]
    public async Task A_valid_first_answer_is_returned_after_one_attempt()
    {
        var client = new ScriptedChatClient("""{"city":"Sydney","temp":21}""");

        var result = await Responder(client).RespondAsync(Request());

        Assert.True(result.Success);
        Assert.Equal(1, result.Attempts);
        Assert.Equal("Sydney", result.Value!.Value.GetProperty("city").GetString());
    }

    [Fact]
    public async Task The_schema_goes_to_the_provider_and_into_the_instruction()
    {
        var client = new ScriptedChatClient("""{"city":"Sydney","temp":21}""");

        await Responder(client).RespondAsync(Request());

        Assert.NotNull(client.Options[0]!.ResponseFormat);
        Assert.Contains("\"required\"", client.Calls[0][0].Text);
        Assert.Equal(ChatRole.System, client.Calls[0][0].Role);
    }

    [Fact]
    public async Task An_invalid_answer_is_sent_back_with_the_errors_and_the_corrected_one_is_accepted()
    {
        var client = new ScriptedChatClient("""{"city":"Sydney"}""", """{"city":"Sydney","temp":21}""");

        var result = await Responder(client).RespondAsync(Request());

        Assert.True(result.Success);
        Assert.Equal(2, result.Attempts);
        var retry = client.Calls[1];
        Assert.Contains(retry, m => m.Role == ChatRole.Assistant && m.Text.Contains("Sydney"));
        Assert.Contains("'temp'", retry[^1].Text);
    }

    [Fact]
    public async Task Text_that_is_not_json_is_rejected_and_retried()
    {
        var client = new ScriptedChatClient("I think it is warm.", """{"city":"Sydney","temp":21}""");

        var result = await Responder(client).RespondAsync(Request());

        Assert.True(result.Success);
        Assert.Contains("not valid JSON", client.Calls[1][^1].Text);
    }

    [Theory]
    [InlineData("```json\n{\"city\":\"Sydney\",\"temp\":21}\n```")]
    [InlineData("Sure! Here you go: {\"city\":\"Sydney\",\"temp\":21} Hope that helps.")]
    public async Task A_code_fence_or_surrounding_words_do_not_fail_a_correct_answer(string reply)
    {
        var result = await Responder(new ScriptedChatClient(reply)).RespondAsync(Request());

        Assert.True(result.Success);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task When_every_attempt_fails_the_result_says_why_and_keeps_the_last_answer()
    {
        var client = new ScriptedChatClient("""{"city":1,"temp":"hot"}""");

        var result = await Responder(client).RespondAsync(Request(attempts: 2));

        Assert.False(result.Success);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, client.Calls.Count);
        Assert.Contains(result.Errors, e => e.Contains("$.city"));
        Assert.Contains("hot", result.RawText);
    }

    [Fact]
    public async Task A_caller_cannot_ask_for_more_attempts_than_the_cap_allows()
    {
        var client = new ScriptedChatClient("nope");

        var result = await Responder(client, o => o.MaxAttemptsCap = 2).RespondAsync(Request(attempts: 50));

        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, client.CallCount());
    }

    [Fact]
    public async Task An_oversized_input_is_refused_without_calling_the_model()
    {
        var client = new ScriptedChatClient("{}");

        var result = await Responder(client, o => o.MaxInputChars = 10).RespondAsync(Request(input: new string('x', 11)));

        Assert.False(result.Success);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task A_schema_that_is_not_an_object_is_refused()
    {
        var client = new ScriptedChatClient("{}");
        var request = new StructuredRequest("x", "y", JsonDocument.Parse("\"string\"").RootElement.Clone());

        var result = await Responder(client).RespondAsync(request);

        Assert.False(result.Success);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Images_travel_with_the_input_as_data_content()
    {
        var client = new ScriptedChatClient("""{"city":"Sydney","temp":21}""");
        var request = Request() with { Images = [new ImageInput(new byte[] { 1, 2, 3 }, "image/png")] };

        await Responder(client).RespondAsync(request);

        var user = client.Calls[0][1];
        Assert.Contains(user.Contents, c => c is DataContent d && d.MediaType == "image/png");
        Assert.Contains(user.Contents, c => c is TextContent);
    }

    private sealed record Weather(string City, double Temp);

    [Fact]
    public async Task A_type_produces_its_own_schema_and_comes_back_typed()
    {
        var client = new ScriptedChatClient("""{"city":"Sydney","temp":21.5}""");

        var result = await Responder(client).RespondAsync<Weather>("Extract the weather.", "It is 21.5 in Sydney.");

        Assert.True(result.Success);
        Assert.Equal(new Weather("Sydney", 21.5), result.Value);
        Assert.Contains("\"city\"", client.Calls[0][0].Text);
    }

    [Fact]
    public async Task A_typed_request_that_never_fits_reports_failure_not_a_default_value()
    {
        var result = await Responder(new ScriptedChatClient("{}")).RespondAsync<Weather>("x", "y", maxAttempts: 1);

        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.NotEmpty(result.Errors);
    }
}

internal static class ScriptedExtensions
{
    public static int CallCount(this ScriptedChatClient client) => client.Calls.Count;
}

public class SchemaCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"schemas-{Guid.NewGuid():N}");

    public SchemaCatalogTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    private SchemaCatalog Catalog() =>
        new(new StructuredOptions { SchemaDirectory = _directory }, NullLogger<SchemaCatalog>.Instance);

    [Fact]
    public void Schema_files_are_loaded_by_file_name_with_their_description()
    {
        File.WriteAllText(Path.Combine(_directory, "invoice.schema.json"), """{"description":"An invoice","type":"object"}""");

        var catalog = Catalog();

        Assert.Equal(["invoice"], catalog.Names);
        Assert.True(catalog.TryGet("INVOICE", out _, out var description));
        Assert.Equal("An invoice", description);
    }

    [Fact]
    public void A_broken_file_is_skipped_and_the_good_ones_still_load()
    {
        File.WriteAllText(Path.Combine(_directory, "bad.schema.json"), "{ not json");
        File.WriteAllText(Path.Combine(_directory, "good.schema.json"), """{"type":"object"}""");

        Assert.Equal(["good"], Catalog().Names);
    }

    [Fact]
    public void A_missing_directory_gives_an_empty_catalog()
    {
        var catalog = new SchemaCatalog(new StructuredOptions { SchemaDirectory = Path.Combine(_directory, "nope") }, NullLogger<SchemaCatalog>.Instance);

        Assert.Empty(catalog.Names);
    }

    [Fact]
    public void Code_can_add_a_schema()
    {
        var catalog = Catalog();

        catalog.Add("point", JsonDocument.Parse("""{"type":"object"}""").RootElement, "A point");

        Assert.True(catalog.TryGet("point", out _, out var description));
        Assert.Equal("A point", description);
    }
}

public class StructuredToolTests
{
    private static (IReadOnlyList<AITool> Tools, ScriptedChatClient Client) Build(string reply)
    {
        var client = new ScriptedChatClient(reply);
        var options = new StructuredOptions { SchemaDirectory = Path.Combine(Path.GetTempPath(), "none-" + Guid.NewGuid().ToString("N")) };
        var catalog = new SchemaCatalog(options, NullLogger<SchemaCatalog>.Instance);
        catalog.Add("weather", JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""").RootElement);
        var responder = new StructuredResponder(client, options, NullLogger<StructuredResponder>.Instance);
        return (StructuredToolProvider.CreateTools(responder, catalog, options), client);
    }

    private static async Task<JsonElement> CallAsync(IReadOnlyList<AITool> tools, string name, Dictionary<string, object?> args)
    {
        var tool = tools.OfType<AIFunction>().Single(t => t.Name == name);
        var result = await tool.InvokeAsync(new AIFunctionArguments(args));
        return JsonDocument.Parse(result is JsonElement e ? e.GetString()! : result!.ToString()!).RootElement;
    }

    [Fact]
    public async Task A_stored_schema_is_used_by_name()
    {
        var (tools, _) = Build("""{"city":"Perth"}""");

        var result = await CallAsync(tools, "extract_structured",
            new() { ["text"] = "Weather in Perth", ["instruction"] = "Find the city", ["schemaName"] = "weather" });

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("Perth", result.GetProperty("value").GetProperty("city").GetString());
    }

    [Fact]
    public async Task An_unknown_schema_name_lists_the_known_ones()
    {
        var (tools, client) = Build("{}");

        var result = await CallAsync(tools, "extract_structured",
            new() { ["text"] = "x", ["instruction"] = "y", ["schemaName"] = "nope" });

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("weather", result.GetProperty("errors")[0].GetString());
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task A_schema_pasted_as_text_works_and_bad_json_is_reported()
    {
        var (tools, _) = Build("""{"n":1}""");

        var good = await CallAsync(tools, "extract_structured",
            new() { ["text"] = "x", ["instruction"] = "y", ["schema"] = """{"type":"object","required":["n"]}""" });
        var bad = await CallAsync(tools, "extract_structured",
            new() { ["text"] = "x", ["instruction"] = "y", ["schema"] = "{ nope" });

        Assert.True(good.GetProperty("success").GetBoolean());
        Assert.False(bad.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Neither_name_nor_schema_is_an_error()
    {
        var (tools, _) = Build("{}");

        var result = await CallAsync(tools, "extract_structured", new() { ["text"] = "x", ["instruction"] = "y" });

        Assert.False(result.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task The_listing_tool_returns_the_catalog()
    {
        var (tools, _) = Build("{}");

        var result = await CallAsync(tools, "list_schemas", []);

        Assert.Equal("weather", result.GetProperty("schemas")[0].GetString());
    }
}
