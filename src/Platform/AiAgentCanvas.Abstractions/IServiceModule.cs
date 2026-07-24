using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Abstractions;

/// <summary>
/// A self-contained module that registers its own services and reads its own
/// configuration. Implemented by agent and data-connection projects so the host
/// can discover and load them without naming them explicitly.
/// </summary>
public interface IServiceModule
{
    /// <summary>
    /// Configuration section name that controls this module (e.g. "Agents:FinancialAnalyst").
    /// The module is skipped when <c>Enabled</c> is <c>false</c> in that section.
    /// </summary>
    string SectionName { get; }

    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
