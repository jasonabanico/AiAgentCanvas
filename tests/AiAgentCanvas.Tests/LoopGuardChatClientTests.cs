using AiAgentCanvas.Orchestration.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace AiAgentCanvas.Tests;

public class LoopGuardChatClientTests
{
    private static readonly ITokenCounter Counter = new TiktokenCounter("gpt-4o");

    private static LoopGuardOptions Options() => new()
    {
        MaxToolRounds = 5,
        MaxRunTokens = 100_000,
        RepeatWarningThreshold = 2,
        RepeatTerminationThreshold = 4,
        StagnationWindow = 3,
    };

    private static ChatOptions WithTools() => new()
    {
        Tools = [AIFunctionFactory.Create(() => "result", "lookup", "Look something up")],
    };

    private static List<ChatMessage> RunWith(int rounds, Func<int, string> argument)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.User, "find the answer"),
        };

        for (var i = 0; i < rounds; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent($"call{i}", "lookup", new Dictionary<string, object?> { ["q"] = argument(i) })]));
            messages.Add(new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent($"call{i}", $"result {i}")]));
        }

        return messages;
    }

    [Fact]
    public async Task Leaves_a_run_under_the_cap_alone()
    {
        var inner = new FakeChatClient();
        var client = new LoopGuardChatClient(inner, Options(), Counter);
        var options = WithTools();

        await client.GetResponseAsync(RunWith(1, i => $"query {i}"), options);

        Assert.NotNull(options.Tools);
        Assert.NotEmpty(options.Tools!);
    }

    [Fact]
    public async Task Withdraws_tools_once_the_round_cap_is_reached()
    {
        var inner = new FakeChatClient();
        var client = new LoopGuardChatClient(inner, Options(), Counter);
        var options = WithTools();

        await client.GetResponseAsync(RunWith(5, i => $"query {i}"), options);

        Assert.Null(options.Tools);
        Assert.Equal(ChatToolMode.None, options.ToolMode);
        Assert.Contains("maximum", inner.LastMessages[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nudges_a_repeated_call_before_stopping_it()
    {
        var inner = new FakeChatClient();
        var client = new LoopGuardChatClient(inner, Options(), Counter);
        var options = WithTools();

        await client.GetResponseAsync(RunWith(2, _ => "same query"), options);

        Assert.NotNull(options.Tools);
        Assert.Contains("already called", inner.LastMessages[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stops_a_run_that_keeps_repeating_the_same_call()
    {
        var inner = new FakeChatClient();
        var client = new LoopGuardChatClient(inner, Options(), Counter);
        var options = WithTools();

        await client.GetResponseAsync(RunWith(4, _ => "same query"), options);

        Assert.Null(options.Tools);
        Assert.Contains("repeated", inner.LastMessages[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stops_a_run_whose_results_stopped_changing()
    {
        var inner = new FakeChatClient();
        var client = new LoopGuardChatClient(inner, Options(), Counter);
        var options = WithTools();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.User, "find the answer"),
        };

        // Distinct arguments, so only the unchanging results can trip the guard.
        for (var i = 0; i < 3; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent($"call{i}", "lookup", new Dictionary<string, object?> { ["q"] = $"variation {i}" })]));
            messages.Add(new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent($"call{i}", "identical result")]));
        }

        await client.GetResponseAsync(messages, options);

        Assert.Null(options.Tools);
        Assert.Contains("identical", inner.LastMessages[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Round_count_resets_on_a_new_user_message()
    {
        var inner = new FakeChatClient();
        var client = new LoopGuardChatClient(inner, Options(), Counter);
        var options = WithTools();

        var messages = RunWith(5, i => $"query {i}");
        messages.Add(new ChatMessage(ChatRole.User, "new question"));

        await client.GetResponseAsync(messages, options);

        Assert.NotNull(options.Tools);
    }

    [Fact]
    public async Task Disabled_guard_changes_nothing()
    {
        var inner = new FakeChatClient();
        var options = Options();
        options.Enabled = false;

        var client = new LoopGuardChatClient(inner, options, Counter);
        var chatOptions = WithTools();

        await client.GetResponseAsync(RunWith(20, _ => "same query"), chatOptions);

        Assert.NotNull(chatOptions.Tools);
    }
}
