#pragma warning disable MEAI001

using System.Text.Json;
using AiAgentCanvas.Orchestration.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiAgentCanvas.Tests;

public class ToolSelectionTests
{
    private static AITool Tool(string name, string description) =>
        AIFunctionFactory.Create(() => "ok", name, description);

    /// <summary>Thirty tools: a few that matter to a message about email and many that do not.</summary>
    private static List<AITool> Registry()
    {
        var tools = new List<AITool>
        {
            Tool("gmail_send_message", "Send an email message through the connected Gmail account"),
            Tool("gmail_search_messages", "Search the connected Gmail mailbox for email messages"),
            Tool("twilio_send_sms", "Send a text message with the connected Twilio account"),
            Tool("market_get_quote", "Get the latest stock price quote for a ticker symbol"),
            Tool("calendar_create_event", "Create a calendar event with attendees"),
        };
        for (var i = 0; i < 25; i++)
            tools.Add(Tool($"filler_tool_{i}", $"Unrelated helper number {i} for housekeeping"));
        return tools;
    }

    private static (ToolSelectingChatClient Client, ScriptedChatClient Inner) Build(
        Action<ToolSelectionOptions>? configure = null, IEmbeddingGenerator<string, Embedding<float>>? embedder = null)
    {
        var options = new ToolSelectionOptions { Enabled = true, MaxTools = 6 };
        configure?.Invoke(options);
        var inner = new ScriptedChatClient("done");
        return (new ToolSelectingChatClient(inner, options, embedder), inner);
    }

    private static List<ChatMessage> UserSays(string text) => [new(ChatRole.User, text)];

    private static string[] Offered(ScriptedChatClient inner, int call = 0) =>
        inner.Options[call]!.Tools!.Select(t => t.Name).ToArray();

    [Fact]
    public async Task A_request_within_the_limit_is_left_alone()
    {
        var (client, inner) = Build(o => o.MaxTools = 40);
        var options = new ChatOptions { Tools = Registry() };

        await client.GetResponseAsync(UserSays("email Bob"), options);

        Assert.Same(options, inner.Options[0]);
    }

    [Fact]
    public async Task A_request_over_the_limit_keeps_only_that_many_tools_and_they_are_the_relevant_ones()
    {
        var (client, inner) = Build();

        await client.GetResponseAsync(UserSays("Please send an email message to Bob"), new ChatOptions { Tools = Registry() });

        var offered = Offered(inner);
        Assert.Equal(6, offered.Length);
        Assert.Contains("gmail_send_message", offered);
        Assert.Contains("gmail_search_messages", offered);
        Assert.Contains("twilio_send_sms", offered);
        Assert.DoesNotContain("filler_tool_20", offered);
    }

    [Fact]
    public async Task Words_in_the_tool_name_count_for_more_than_words_in_its_description()
    {
        var (client, inner) = Build(o => o.MaxTools = 1);
        var tools = new List<AITool>
        {
            Tool("a_helper", "Handles invoice lookups for the finance team"),
            Tool("invoice_lookup", "Finds one record"),
            Tool("b_helper", "Does something else entirely"),
        };

        await client.GetResponseAsync(UserSays("find the invoice"), new ChatOptions { Tools = tools });

        Assert.Equal(["invoice_lookup"], Offered(inner));
    }

    [Fact]
    public async Task The_agents_own_options_keep_the_full_list_for_the_next_run()
    {
        var (client, _) = Build();
        var options = new ChatOptions { Tools = Registry() };

        await client.GetResponseAsync(UserSays("send an email message"), options);

        Assert.Equal(30, options.Tools!.Count);
    }

    [Fact]
    public async Task The_registered_order_is_kept()
    {
        var (client, inner) = Build();
        var registry = Registry();

        await client.GetResponseAsync(UserSays("send an email message"), new ChatOptions { Tools = registry });

        var order = registry.Select(t => t.Name).ToList();
        var offered = Offered(inner);
        Assert.Equal(offered.OrderBy(order.IndexOf), offered);
    }

    [Fact]
    public async Task A_tool_the_run_has_already_called_stays_offered_on_later_rounds()
    {
        var (client, inner) = Build();
        List<ChatMessage> history =
        [
            new(ChatRole.User, "send an email message"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "filler_tool_7")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "ok")]),
        ];

        await client.GetResponseAsync(history, new ChatOptions { Tools = Registry() });

        Assert.Contains("filler_tool_7", Offered(inner));
    }

    [Fact]
    public async Task The_same_message_gives_the_same_tools_on_every_round_of_a_run()
    {
        var (client, inner) = Build();
        var first = UserSays("send an email message");
        List<ChatMessage> later =
        [
            new(ChatRole.User, "send an email message"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "gmail_search_messages")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "three results")]),
        ];

        await client.GetResponseAsync(first, new ChatOptions { Tools = Registry() });
        await client.GetResponseAsync(later, new ChatOptions { Tools = Registry() });

        Assert.Equal(Offered(inner, 0), Offered(inner, 1));
    }

    [Fact]
    public async Task Always_included_tools_are_offered_by_name_and_by_family()
    {
        var (client, inner) = Build(o => o.AlwaysInclude = ["market_get_quote", "calendar_*"]);

        await client.GetResponseAsync(UserSays("send an email message"), new ChatOptions { Tools = Registry() });

        var offered = Offered(inner);
        Assert.Contains("market_get_quote", offered);
        Assert.Contains("calendar_create_event", offered);
        Assert.Contains("gmail_send_message", offered);
        Assert.Equal(6, offered.Length);
    }

    [Fact]
    public async Task When_the_required_tools_fill_the_limit_nothing_else_is_added()
    {
        var (client, inner) = Build(o => { o.MaxTools = 2; o.AlwaysInclude = ["filler_tool_1*"]; });

        await client.GetResponseAsync(UserSays("send an email message"), new ChatOptions { Tools = Registry() });

        Assert.All(Offered(inner), name => Assert.StartsWith("filler_tool_1", name));
    }

    [Fact]
    public void Selection_is_off_until_it_is_turned_on()
    {
        Assert.False(new ToolSelectionOptions().Enabled);
    }

    [Fact]
    public async Task Nothing_changes_when_selection_is_off_or_there_is_no_user_message()
    {
        var (off, offInner) = Build(o => o.Enabled = false);
        var (client, inner) = Build();
        var options = new ChatOptions { Tools = Registry() };

        await off.GetResponseAsync(UserSays("send an email message"), options);
        await client.GetResponseAsync([new ChatMessage(ChatRole.Assistant, "hello")], options);

        Assert.Same(options, offInner.Options[0]);
        Assert.Same(options, inner.Options[0]);
    }

    [Fact]
    public async Task A_message_that_matches_nothing_keeps_the_first_tools_in_registered_order()
    {
        var (client, inner) = Build(o => o.MaxTools = 3);

        await client.GetResponseAsync(UserSays("zzzz qqqq"), new ChatOptions { Tools = Registry() });

        Assert.Equal(["gmail_send_message", "gmail_search_messages", "twilio_send_sms"], Offered(inner));
    }

    [Fact]
    public async Task The_streaming_path_selects_too()
    {
        var (client, inner) = Build();

        await foreach (var _ in client.GetStreamingResponseAsync(UserSays("send an email message"), new ChatOptions { Tools = Registry() }))
        {
        }

        Assert.Equal(6, Offered(inner).Length);
    }

    [Fact]
    public async Task With_an_embedding_model_the_tools_are_embedded_once_and_reused()
    {
        var embedder = new HashEmbeddingGenerator();
        var (client, _) = Build(embedder: embedder);

        await client.GetResponseAsync(UserSays("send an email message"), new ChatOptions { Tools = Registry() });
        var afterFirst = embedder.BatchSizes.Count;
        await client.GetResponseAsync(UserSays("send an email message"), new ChatOptions { Tools = Registry() });

        Assert.True(afterFirst >= 2, "The query and the tools are embedded on the first request.");
        Assert.Equal(afterFirst, embedder.BatchSizes.Count);
    }

    [Fact]
    public async Task A_failing_embedding_model_falls_back_to_the_keyword_ranking()
    {
        var embedder = new HashEmbeddingGenerator { FailOnCall = 2 };
        var (client, inner) = Build(embedder: embedder);

        await client.GetResponseAsync(UserSays("send an email message"), new ChatOptions { Tools = Registry() });

        Assert.Contains("gmail_send_message", Offered(inner));
    }

    [Fact]
    public async Task An_embedding_model_is_ignored_when_the_option_is_off()
    {
        var embedder = new HashEmbeddingGenerator();
        var (client, _) = Build(o => o.UseEmbeddings = false, embedder);

        await client.GetResponseAsync(UserSays("send an email message"), new ChatOptions { Tools = Registry() });

        Assert.Empty(embedder.BatchSizes);
    }
}

public class ToolOutputLimitTests
{
    private static AIFunction Bounded(Func<object?> produce, int max, bool enabled = true) =>
        new BoundedOutputAIFunction(
            AIFunctionFactory.Create(produce, "t", "d"),
            new ToolOutputOptions { MaxChars = max, Enabled = enabled });

    private static async Task<object?> Run(AIFunction fn) => await fn.InvokeAsync(new AIFunctionArguments());

    [Fact]
    public async Task A_result_within_the_limit_passes_through_unchanged()
    {
        var result = await Run(Bounded(() => "short result", 100));

        Assert.Equal("short result", Text(result));
    }

    [Fact]
    public async Task A_longer_result_is_cut_and_the_cut_is_marked()
    {
        var result = Text(await Run(Bounded(() => new string('x', 500), 100)));

        Assert.StartsWith(new string('x', 100), result);
        Assert.DoesNotContain(new string('x', 101), result);
        Assert.Contains("output cut: showing 100 of 500 characters", result);
    }

    [Fact]
    public async Task A_structured_result_is_measured_by_its_json()
    {
        var big = Enumerable.Range(0, 200).Select(i => new { index = i, text = "padding padding padding" }).ToList();

        var result = Text(await Run(Bounded(() => big, 300)));

        Assert.Contains("output cut", result);
        Assert.True(result.Length < 500);
    }

    [Fact]
    public async Task A_small_structured_result_is_not_touched()
    {
        var result = await Run(Bounded(() => new { ok = true }, 300));

        Assert.DoesNotContain("output cut", Text(result));
    }

    [Fact]
    public async Task The_limit_can_be_turned_off()
    {
        var result = Text(await Run(Bounded(() => new string('x', 500), 100, enabled: false)));

        Assert.Equal(500, result.Length);
    }

    [Fact]
    public async Task Tools_are_wrapped_with_the_limit_when_the_options_are_registered()
    {
        var services = new ServiceCollection().AddSingleton(new ToolOutputOptions { MaxChars = 10 }).BuildServiceProvider();
        var raw = AIFunctionFactory.Create(() => new string('y', 50), "wide", "A tool with a wide result");

        var wrapped = (AIFunction)AgentPipeline.WrapTools(services, [raw]).Single();

        var result = Text(await wrapped.InvokeAsync(new AIFunctionArguments()));
        Assert.Contains("output cut", result);
    }

    private static string Text(object? result) => result switch
    {
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString()!,
        JsonElement e => e.GetRawText(),
        _ => result?.ToString() ?? "",
    };
}
