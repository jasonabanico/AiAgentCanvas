namespace AiAgentCanvas.Abstractions;

/// <summary>
/// Durable position for anything that reads a feed in pages: a connector polling for
/// new messages, an ingest job walking a change list. Without it a restart either
/// replays the whole feed or skips what arrived while the process was down.
/// </summary>
public interface ICursorStore
{
    string? Get(string key);

    void Set(string key, string value);
}
