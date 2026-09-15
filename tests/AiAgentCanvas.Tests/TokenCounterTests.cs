using AiAgentCanvas.Orchestration.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace AiAgentCanvas.Tests;

public class TokenCounterTests
{
    private readonly ITokenCounter _counter = new TiktokenCounter("gpt-4o");

    [Fact]
    public void Empty_text_costs_nothing()
    {
        Assert.Equal(0, _counter.Count(null));
        Assert.Equal(0, _counter.Count(string.Empty));
    }

    [Fact]
    public void Longer_text_costs_more()
    {
        var shortText = _counter.Count("hello world");
        var longText = _counter.Count(string.Join(" ", Enumerable.Repeat("hello world", 100)));

        Assert.True(longText > shortText);
    }

    [Fact]
    public void Unknown_model_names_fall_back_instead_of_throwing()
    {
        var counter = new TiktokenCounter("databricks-dbrx-instruct");
        Assert.True(counter.Count("hello world") > 0);
    }

    [Fact]
    public void Message_count_includes_framing_overhead()
    {
        var message = new ChatMessage(ChatRole.User, "hello");
        Assert.True(_counter.CountMessage(message) > _counter.Count("hello"));
    }

    [Fact]
    public void Counts_tool_calls_and_results()
    {
        var call = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("id", "lookup", new Dictionary<string, object?> { ["query"] = "a long search string" })]);
        var result = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("id", "a long result payload")]);

        Assert.True(_counter.CountMessage(call) > 4);
        Assert.True(_counter.CountMessage(result) > 4);
    }

    [Fact]
    public void Tool_definitions_cost_more_than_their_names()
    {
        var tool = AIFunctionFactory.Create(
            (string query, int limit) => "result",
            "search_documents",
            "Search the indexed corpus and return matching passages.");

        Assert.True(_counter.CountTools([tool]) > _counter.Count("search_documents"));
    }

    [Fact]
    public void Json_is_denser_than_the_four_characters_per_token_rule()
    {
        // The heuristic the guide warns about would badly under-count this.
        var json = """{"id":1,"name":"widget","tags":["a","b"],"nested":{"x":1.5,"y":-2}}""";

        Assert.True(_counter.Count(json) > json.Length / 4);
    }
}
