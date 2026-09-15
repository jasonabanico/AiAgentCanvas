using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Tests;

/// <summary>
/// Records what the pipeline actually sent to the provider, which is the only way
/// to assert that a delegating client trimmed, compacted, or withdrew anything.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly string _reply;

    public FakeChatClient(string reply = "ok") => _reply = reply;

    public List<ChatMessage> LastMessages { get; private set; } = [];
    public ChatOptions? LastOptions { get; private set; }
    public int CallCount { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        LastOptions = options;
        CallCount++;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _reply)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var message in response.Messages)
            yield return new ChatResponseUpdate(message.Role, message.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
