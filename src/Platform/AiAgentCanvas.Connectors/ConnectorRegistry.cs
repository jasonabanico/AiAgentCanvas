namespace AiAgentCanvas.Connectors;

/// <summary>The connector types this host can run, by id.</summary>
public sealed class ConnectorRegistry
{
    private readonly Dictionary<string, IConnectorDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    public ConnectorRegistry(IEnumerable<IConnectorDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            var id = definition.Descriptor.Id;
            if (!_definitions.TryAdd(id, definition))
                throw new InvalidOperationException($"Two connectors are registered with the id '{id}'.");
        }
    }

    public IConnectorDefinition? Get(string id) => _definitions.GetValueOrDefault(id);

    public IReadOnlyList<ConnectorDescriptor> List() =>
        _definitions.Values.Select(d => d.Descriptor).OrderBy(d => d.Category).ThenBy(d => d.Id).ToList();
}
