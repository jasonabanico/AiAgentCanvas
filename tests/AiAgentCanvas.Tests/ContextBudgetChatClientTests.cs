using AiAgentCanvas.Orchestration.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace AiAgentCanvas.Tests;

public class ContextBudgetChatClientTests
{
    private static readonly ITokenCounter Counter = new TiktokenCounter("gpt-4o");

    private static ContextBudgetOptions TightBudget() => new()
    {
        MaxContextTokens = 2_000,
        ReservedOutputTokens = 500,
        HistoryFraction = 0.30,
        InstructionsFraction = 0.30,
        ToolsFraction = 0.10,
        KeepRecentMessages = 4,
        SummarizeDroppedHistory = false,
    };

    private static List<ChatMessage> LongConversation(int turns, int wordsPerTurn = 120)
    {
        var filler = string.Join(" ", Enumerable.Repeat("token", wordsPerTurn));
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a test agent."),
            new(ChatRole.User, "Original task: keep this pinned."),
        };

        for (var i = 0; i < turns; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, $"turn {i} {filler}"));
            messages.Add(new ChatMessage(ChatRole.User, $"follow up {i} {filler}"));
        }

        return messages;
    }

    [Fact]
    public async Task Short_conversations_pass_through_untouched()
    {
        var inner = new FakeChatClient();
        var client = new ContextBudgetChatClient(inner, TightBudget(), Counter);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.User, "hello"),
        };

        await client.GetResponseAsync(messages);

        Assert.Equal(2, inner.LastMessages.Count);
    }

    [Fact]
    public async Task Compacts_history_that_exceeds_its_ceiling()
    {
        var inner = new FakeChatClient();
        var options = TightBudget();
        var client = new ContextBudgetChatClient(inner, options, Counter);
        var messages = LongConversation(turns: 20);

        Assert.True(Counter.CountMessages(messages) > options.InputBudget * options.HistoryFraction);

        await client.GetResponseAsync(messages);

        Assert.True(inner.LastMessages.Count < messages.Count);
        Assert.True(Counter.CountMessages(inner.LastMessages) <= options.InputBudget);
    }

    [Fact]
    public async Task Keeps_the_system_message_and_the_original_task()
    {
        var inner = new FakeChatClient();
        var client = new ContextBudgetChatClient(inner, TightBudget(), Counter);

        await client.GetResponseAsync(LongConversation(turns: 20));

        Assert.Equal(ChatRole.System, inner.LastMessages[0].Role);
        Assert.Contains("keep this pinned", inner.LastMessages[1].Text);
    }

    [Fact]
    public async Task Keeps_the_most_recent_turn()
    {
        var inner = new FakeChatClient();
        var client = new ContextBudgetChatClient(inner, TightBudget(), Counter);
        var messages = LongConversation(turns: 20);

        await client.GetResponseAsync(messages);

        Assert.Equal(messages[^1].Text, inner.LastMessages[^1].Text);
    }

    [Fact]
    public async Task Never_leaves_a_tool_result_without_its_call()
    {
        var inner = new FakeChatClient();
        var client = new ContextBudgetChatClient(inner, TightBudget(), Counter);

        var filler = string.Join(" ", Enumerable.Repeat("token", 150));
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.User, "task"),
        };

        for (var i = 0; i < 15; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent($"call{i}", "lookup", new Dictionary<string, object?> { ["q"] = filler })]));
            messages.Add(new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent($"call{i}", filler)]));
        }

        await client.GetResponseAsync(messages);

        var callIds = inner.LastMessages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(c => c.CallId)
            .ToHashSet();

        var orphanResults = inner.LastMessages
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .Where(r => !callIds.Contains(r.CallId))
            .ToList();

        Assert.Empty(orphanResults);
    }

    [Fact]
    public async Task Trims_instructions_that_blow_their_ceiling()
    {
        var inner = new FakeChatClient();
        var options = TightBudget();
        var client = new ContextBudgetChatClient(inner, options, Counter);

        var bloated = string.Join(" ", Enumerable.Repeat("context", 4_000));
        var chatOptions = new ChatOptions { Instructions = bloated };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], chatOptions);

        var ceiling = (int)(options.InputBudget * options.InstructionsFraction);
        Assert.True(Counter.Count(chatOptions.Instructions) <= ceiling + 32);
        Assert.Contains("truncated", chatOptions.Instructions!);
    }

    [Fact]
    public async Task Disabled_budget_changes_nothing()
    {
        var inner = new FakeChatClient();
        var options = TightBudget();
        options.Enabled = false;

        var client = new ContextBudgetChatClient(inner, options, Counter);
        var messages = LongConversation(turns: 20);

        await client.GetResponseAsync(messages);

        Assert.Equal(messages.Count, inner.LastMessages.Count);
    }
}
