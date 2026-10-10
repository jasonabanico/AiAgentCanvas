#pragma warning disable MEAI001, MAAI001

using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Orchestration;
using AiAgentCanvas.Orchestration.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiAgentCanvas.Tests;

/// <summary>
/// A tool added to the registry while the host runs must reach the agents. These tests build
/// the real default agent and the real persona agents, run a turn, and read what the model
/// was offered, so they fail if the provider is not wired in.
/// </summary>
public class DynamicToolDeliveryTests
{
    private static AIFunction Tool(string name) => AIFunctionFactory.Create(() => "ok", name, $"The {name} tool.");

    private sealed class Rig : IDisposable
    {
        public ScriptedChatClient Model { get; } = new("fine");
        public ServiceProvider Services { get; }
        public DynamicToolRegistry Registry => Services.GetRequiredService<DynamicToolRegistry>();

        public Rig(IReadOnlyList<AITool>? startupTools = null, IAgentToolsSeed[]? seeds = null, string[]? personas = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IChatClient>(Model);

            if (startupTools is not null)
                services.AddSingleton(startupTools);

            foreach (var seed in seeds ?? [])
                services.AddSingleton(seed);

            services.AddAiAgentCanvas(new ConfigurationBuilder().Build());

            var known = (personas ?? []).ToDictionary(p => p, p => new AgentPersonaInfo { Name = p, Description = p, Instructions = $"You are {p}." });
            services.AddAiAgentCanvasInterAgentCommunication(
                _ => name => known.GetValueOrDefault(name),
                _ => () => known.Values);

            Services = services.BuildServiceProvider();
        }

        public async Task<List<string>> OfferedAsync(AIAgent agent)
        {
            Model.Options.Clear();
            await agent.RunAsync("hello");
            return Model.Options.LastOrDefault()?.Tools?.Select(t => t.Name).ToList() ?? [];
        }

        public void Dispose() => Services.Dispose();
    }

    [Fact]
    public async Task A_tool_registered_after_the_agent_was_built_is_offered_to_the_model()
    {
        using var rig = new Rig();
        var agent = rig.Services.GetRequiredService<AIAgent>();

        rig.Registry.Register("connector:abc", [Tool("twilio_sms_list")]);
        var offered = await rig.OfferedAsync(agent);

        Assert.Contains("twilio_sms_list", offered);
    }

    [Fact]
    public async Task A_tool_that_is_removed_is_no_longer_offered()
    {
        using var rig = new Rig();
        var agent = rig.Services.GetRequiredService<AIAgent>();
        rig.Registry.Register("connector:abc", [Tool("twilio_sms_list")]);
        Assert.Contains("twilio_sms_list", await rig.OfferedAsync(agent));

        rig.Registry.Unregister("connector:abc");

        Assert.DoesNotContain("twilio_sms_list", await rig.OfferedAsync(agent));
    }

    [Fact]
    public async Task The_agents_own_tools_are_still_offered_beside_the_runtime_ones()
    {
        using var rig = new Rig(startupTools: [Tool("startup_tool")]);
        var agent = rig.Services.GetRequiredService<AIAgent>();
        rig.Registry.Register("mcp:x", [Tool("runtime_tool")]);

        var offered = await rig.OfferedAsync(agent);

        Assert.Contains("startup_tool", offered);
        Assert.Contains("runtime_tool", offered);
    }

    [Fact]
    public async Task A_runtime_tool_with_the_name_of_a_startup_tool_does_not_appear_twice()
    {
        using var rig = new Rig(startupTools: [Tool("shared_name")]);
        var agent = rig.Services.GetRequiredService<AIAgent>();
        rig.Registry.Register("mcp:x", [Tool("shared_name")]);

        var offered = await rig.OfferedAsync(agent);

        Assert.Single(offered, n => n == "shared_name");
    }

    [Fact]
    public async Task A_tool_seed_for_the_default_agent_limits_the_runtime_tools_it_sees()
    {
        using var rig = new Rig(seeds: [new AgentToolsSeed("AiAgentCanvas", ["allowed_tool"])]);
        var agent = rig.Services.GetRequiredService<AIAgent>();
        rig.Registry.Register("mcp:x", [Tool("allowed_tool"), Tool("other_tool")]);

        var offered = await rig.OfferedAsync(agent);

        Assert.Contains("allowed_tool", offered);
        Assert.DoesNotContain("other_tool", offered);
    }

    [Fact]
    public async Task A_persona_agent_sees_runtime_tools_when_it_has_no_seed()
    {
        using var rig = new Rig(personas: ["analyst"]);
        var agent = rig.Services.GetRequiredService<AgentRegistry>().Resolve("analyst")!;
        rig.Registry.Register("connector:abc", [Tool("twilio_sms_list")]);

        Assert.Contains("twilio_sms_list", await rig.OfferedAsync(agent));
    }

    [Fact]
    public async Task A_persona_agent_with_a_seed_sees_only_the_runtime_tools_the_seed_names()
    {
        using var rig = new Rig(
            seeds: [new AgentToolsSeed("responder", ["twilio_sms_send"])],
            personas: ["responder"]);
        var agent = rig.Services.GetRequiredService<AgentRegistry>().Resolve("responder")!;
        rig.Registry.Register("connector:abc", [Tool("twilio_sms_send"), Tool("twilio_sms_list")]);

        var offered = await rig.OfferedAsync(agent);

        Assert.Contains("twilio_sms_send", offered);
        Assert.DoesNotContain("twilio_sms_list", offered);
    }

    [Fact]
    public async Task A_persona_agent_picks_up_a_tool_registered_after_it_was_built()
    {
        using var rig = new Rig(personas: ["analyst"]);
        var agent = rig.Services.GetRequiredService<AgentRegistry>().Resolve("analyst")!;
        await rig.OfferedAsync(agent);

        rig.Registry.Register("mcp:late", [Tool("late_tool")]);

        Assert.Contains("late_tool", await rig.OfferedAsync(agent));
    }
}
