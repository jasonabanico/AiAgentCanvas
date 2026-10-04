namespace AiAgentCanvas.Abstractions;

/// <summary>
/// Something that happened in an outside service: a text arrived, a call was missed, a
/// payment failed. <see cref="Summary"/> and <see cref="Data"/> come from the outside
/// service and are untrusted input. Treat them as data, never as instructions.
/// </summary>
/// <param name="Id">Identifies the occurrence, for de-duplication. Stable across redeliveries.</param>
/// <param name="Type">A dotted name such as <c>sms.received</c> or <c>call.missed</c>.</param>
public sealed record ConnectorEvent(
    string Id,
    string Type,
    DateTimeOffset At,
    string Summary,
    IReadOnlyDictionary<string, string> Data);

public enum ConnectorEventOutcome
{
    /// <summary>At least one subscriber took the event.</summary>
    Accepted,

    /// <summary>Every subscriber had already seen it.</summary>
    Duplicate,

    /// <summary>Nothing is subscribed to this connection or event type.</summary>
    NoSubscribers,

    /// <summary>A subscriber's backlog is full. The sender should retry.</summary>
    Rejected,
}

/// <summary>Where connector events go. The event trigger capability implements it.</summary>
public interface IConnectorEventSink
{
    ConnectorEventOutcome Publish(string connectionId, ConnectorEvent evt);
}
