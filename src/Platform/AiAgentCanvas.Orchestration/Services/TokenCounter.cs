using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;

namespace AiAgentCanvas.Orchestration.Services;

/// <summary>
/// Counts prompt tokens. Budget enforcement is only as good as the count behind it,
/// so this uses the model's own BPE tokenizer rather than a characters-per-token
/// approximation, which drifts badly on code and JSON.
/// </summary>
public interface ITokenCounter
{
    int Count(string? text);
    int CountMessage(ChatMessage message);
    int CountMessages(IEnumerable<ChatMessage> messages);
    int CountTools(IEnumerable<AITool>? tools);
}

public sealed class TiktokenCounter : ITokenCounter
{
    // Per-message framing overhead in the chat wire format (role, delimiters).
    private const int MessageOverheadTokens = 4;

    private readonly Tokenizer _tokenizer;

    public TiktokenCounter(string? modelName = null)
    {
        _tokenizer = CreateTokenizer(modelName);
    }

    private static Tokenizer CreateTokenizer(string? modelName)
    {
        if (!string.IsNullOrWhiteSpace(modelName))
        {
            try
            {
                return TiktokenTokenizer.CreateForModel(modelName);
            }
            catch (Exception)
            {
                // Unknown deployment name (common for Databricks and Cortex endpoints).
                // cl100k_base is close enough for budgeting and never throws.
            }
        }

        return TiktokenTokenizer.CreateForEncoding("cl100k_base");
    }

    public int Count(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : _tokenizer.CountTokens(text);

    public int CountMessage(ChatMessage message)
    {
        var total = MessageOverheadTokens;

        foreach (var content in message.Contents)
        {
            total += content switch
            {
                TextContent text => Count(text.Text),
                FunctionCallContent call => Count(call.Name) + Count(SerializeArguments(call)),
                FunctionResultContent result => Count(result.Result?.ToString()),
                _ => Count(content.ToString()),
            };
        }

        return total;
    }

    public int CountMessages(IEnumerable<ChatMessage> messages) =>
        messages.Sum(CountMessage);

    public int CountTools(IEnumerable<AITool>? tools)
    {
        if (tools is null) return 0;

        var total = 0;
        foreach (var tool in tools)
        {
            total += Count(tool.Name) + Count(tool.Description);
            if (tool is AIFunction function)
                total += Count(function.JsonSchema.ToString());
        }
        return total;
    }

    private static string SerializeArguments(FunctionCallContent call)
    {
        if (call.Arguments is null || call.Arguments.Count == 0)
            return string.Empty;

        return string.Join(",", call.Arguments.Select(a => $"{a.Key}:{a.Value}"));
    }
}
