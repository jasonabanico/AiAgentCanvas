using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AiAgentCanvas.Abstractions;
using DataConnection.VectorStore.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Tests;

/// <summary>
/// Turns words into a vector by hashing each into one of 32 slots, so two texts that share
/// words are close. It lets the tests exercise retrieval without a model.
/// </summary>
public sealed class HashEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int Dimensions = 32;

    public List<int> BatchSizes { get; } = [];

    /// <summary>When set, the call with this 1-based number throws.</summary>
    public int? FailOnCall { get; set; }

    public static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (Match word in Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]+"))
        {
            var slot = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(word.Value)), 0) % Dimensions;
            vector[slot] += 1f;
        }

        var length = MathF.Sqrt(vector.Sum(v => v * v));
        if (length > 0)
        {
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= length;
        }
        return vector;
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        BatchSizes.Add(list.Count);
        if (FailOnCall == BatchSizes.Count)
            throw new InvalidOperationException("The embedding model is unavailable.");

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list.Select(v => new Embedding<float>(Embed(v)))));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>A SQLite vector store in a temporary file, deleted with the test.</summary>
public sealed class TempVectorStore : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"vec-{Guid.NewGuid():N}.db");

    public string ConnectionString => $"Data Source={_path}";

    public SqliteDocumentCollection Collection { get; }

    public TempVectorStore()
    {
        Collection = new SqliteDocumentCollection(ConnectionString);
        Collection.EnsureCollectionExistsAsync().GetAwaiter().GetResult();
    }

    public static DocumentRecord Record(
        string id, string text, float[]? vector = null, string? source = null, string? tags = null,
        DateTimeOffset? indexedAt = null, int version = 1) => new()
    {
        Id = id,
        Text = text,
        Source = source,
        Tags = tags,
        IndexedAt = indexedAt,
        Version = version,
        Embedding = vector ?? HashEmbeddingGenerator.Embed(text),
    };

    public void Dispose()
    {
        Collection.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
