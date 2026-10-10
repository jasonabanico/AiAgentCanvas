using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace AiAgentCanvas.Capabilities.Rag;

public sealed class RagIngestionException(string message) : Exception(message);

/// <summary>What one ingest wrote.</summary>
/// <param name="Version">1 for a new source, and one more each time the source is indexed again.</param>
/// <param name="ReplacedChunks">Chunks of earlier versions that this version replaced.</param>
public sealed record IngestResult(string Source, int Version, int Chunks, int ReplacedChunks);

/// <summary>
/// Turns a document into searchable chunks: split, embed, store. Indexing a source again
/// replaces its earlier version. The new chunks are written and embedded before the old ones
/// are removed, so a failed embedding call leaves the document as it was and not half
/// replaced.
/// </summary>
public sealed class RagIngestionService
{
    private readonly VectorStoreCollection<string, DocumentRecord> _collection;
    private readonly IDocumentIndex _index;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embedder;
    private readonly DocumentChunker _chunker;
    private readonly RagOptions _options;
    private readonly ILogger<RagIngestionService> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RagIngestionService(
        VectorStoreCollection<string, DocumentRecord> collection,
        IDocumentIndex index,
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        DocumentChunker chunker,
        RagOptions options,
        ILogger<RagIngestionService> logger,
        TimeProvider? time = null)
    {
        _collection = collection;
        _index = index;
        _embedder = embedder;
        _chunker = chunker;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<IngestResult> IngestAsync(string source, string text, string? tags = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new RagIngestionException("Name the source. It identifies the document, so indexing it again replaces this version.");

        source = source.Trim();
        if (source.Length > 500)
            throw new RagIngestionException("The source name is longer than 500 characters.");

        if (string.IsNullOrWhiteSpace(text))
            throw new RagIngestionException("The document has no text.");

        if (text.Length > _options.MaxDocumentChars)
            throw new RagIngestionException($"The document is {text.Length:N0} characters. The limit is {_options.MaxDocumentChars:N0}.");

        var chunks = _chunker.Chunk(text, source);
        if (chunks.Count == 0)
            throw new RagIngestionException("The document has no usable text. Chunks shorter than 20 characters are dropped.");

        // One ingest at a time, so two requests for the same source cannot pick the same version.
        await _gate.WaitAsync(ct);
        try
        {
            var vectors = await EmbedAsync(chunks.Select(c => c.Text).ToList(), ct);
            var version = await _index.LatestVersionAsync(source, ct) + 1;
            var now = _time.GetUtcNow();

            var records = chunks.Select((chunk, i) => new DocumentRecord
            {
                Id = $"{source}#v{version}#{chunk.Index}",
                Text = chunk.Text,
                Source = source,
                Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim(),
                MetadataJson = JsonSerializer.Serialize(new { chunk = chunk.Index, chunks = chunks.Count }),
                IndexedAt = now,
                Version = version,
                Embedding = vectors[i],
            }).ToList();

            await _collection.UpsertAsync(records, ct);
            var replaced = await _index.DeleteOlderVersionsAsync(source, version, ct);

            _logger.LogInformation("Indexed '{Source}' as version {Version}: {Chunks} chunks, {Replaced} replaced",
                source, version, records.Count, replaced);
            return new IngestResult(source, version, records.Count, replaced);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<int> DeleteAsync(string source, CancellationToken ct = default) =>
        _index.DeleteDocumentAsync(source, ct);

    private async Task<List<ReadOnlyMemory<float>>> EmbedAsync(List<string> texts, CancellationToken ct)
    {
        var vectors = new List<ReadOnlyMemory<float>>(texts.Count);
        var batch = Math.Max(1, _options.EmbeddingBatchSize);

        for (var i = 0; i < texts.Count; i += batch)
        {
            var slice = texts.Skip(i).Take(batch).ToList();
            var generated = await _embedder.GenerateAsync(slice, cancellationToken: ct);
            if (generated.Count != slice.Count)
                throw new RagIngestionException($"The embedding model returned {generated.Count} vectors for {slice.Count} chunks.");

            vectors.AddRange(generated.Select(e => e.Vector));
        }

        return vectors;
    }
}
