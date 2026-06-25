using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Core.Skills;

public sealed class DynamicToolRegistry
{
    private readonly ConcurrentDictionary<string, List<AITool>> _toolsBySource = new();

    public void Register(string source, IEnumerable<AITool> tools)
    {
        _toolsBySource[source] = tools.ToList();
    }

    public void Unregister(string source)
    {
        _toolsBySource.TryRemove(source, out _);
    }

    public IReadOnlyList<AITool> GetAllTools()
    {
        return _toolsBySource.Values.SelectMany(t => t).ToList();
    }
}
