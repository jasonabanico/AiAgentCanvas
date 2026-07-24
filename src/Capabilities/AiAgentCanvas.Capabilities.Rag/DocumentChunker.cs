using System.Text;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.Capabilities.Rag;

public sealed class DocumentChunker
{
    private static readonly string[] ParagraphSeparators = ["\n\n", "\r\n\r\n"];
    private static readonly string[] SentenceEndings = [". ", "! ", "? ", ".\n", "!\n", "?\n"];

    public int ChunkSize { get; init; } = 512;
    public int ChunkOverlap { get; init; } = 64;

    public List<DocumentChunk> Chunk(string text, string? source = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var paragraphs = SplitByParagraphs(text);
        var chunks = new List<DocumentChunk>();
        var buffer = new StringBuilder();
        var index = 0;

        foreach (var paragraph in paragraphs)
        {
            if (buffer.Length + paragraph.Length > ChunkSize && buffer.Length > 0)
            {
                chunks.Add(new DocumentChunk { Text = buffer.ToString().Trim(), Index = index++, Source = source });
                var overlap = GetOverlapText(buffer.ToString());
                buffer.Clear();
                buffer.Append(overlap);
            }

            if (paragraph.Length > ChunkSize)
            {
                if (buffer.Length > 0)
                {
                    chunks.Add(new DocumentChunk { Text = buffer.ToString().Trim(), Index = index++, Source = source });
                    buffer.Clear();
                }

                foreach (var sentenceChunk in SplitBySentences(paragraph))
                {
                    chunks.Add(new DocumentChunk { Text = sentenceChunk.Trim(), Index = index++, Source = source });
                }
            }
            else
            {
                if (buffer.Length > 0) buffer.Append("\n\n");
                buffer.Append(paragraph);
            }
        }

        if (buffer.Length > 0)
            chunks.Add(new DocumentChunk { Text = buffer.ToString().Trim(), Index = index, Source = source });

        return chunks.Where(c => c.Text.Length > 20).ToList();
    }

    private static List<string> SplitByParagraphs(string text)
    {
        var parts = text.Split(ParagraphSeparators, StringSplitOptions.RemoveEmptyEntries);
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    private List<string> SplitBySentences(string text)
    {
        var sentences = new List<string>();
        var buffer = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            buffer.Append(text[i]);

            var isSentenceEnd = SentenceEndings.Any(ending =>
            {
                if (i + ending.Length > text.Length) return false;
                return text.Substring(i, ending.Length) == ending;
            });

            if (isSentenceEnd || buffer.Length >= ChunkSize)
            {
                sentences.Add(buffer.ToString());
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
            sentences.Add(buffer.ToString());

        var result = new List<string>();
        buffer.Clear();

        foreach (var sentence in sentences)
        {
            if (buffer.Length + sentence.Length > ChunkSize && buffer.Length > 0)
            {
                result.Add(buffer.ToString());
                var overlap = GetOverlapText(buffer.ToString());
                buffer.Clear();
                buffer.Append(overlap);
            }
            buffer.Append(sentence);
        }

        if (buffer.Length > 0)
            result.Add(buffer.ToString());

        return result;
    }

    private string GetOverlapText(string text)
    {
        if (text.Length <= ChunkOverlap)
            return text;

        return text[^ChunkOverlap..];
    }
}
