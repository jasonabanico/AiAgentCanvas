using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HelloWorldAgent;

public static class HelloWorldServiceExtensions
{
    public static IServiceCollection AddHelloWorldAgent(this IServiceCollection services)
    {
        // 1. Register tools — the agent can call these at runtime
        services.AddSingleton<HelloWorldToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<HelloWorldToolProvider>().GetTools());

        // 2. Seed a persona — gives the agent a personality and tells it about its tools.
        //    Users can activate it with: "switch to the hello-world persona"
        services.AddSingleton<IPersonaSeed>(new PersonaSeed(
            name: "hello-world",
            description: "A friendly demo agent that greets users, rolls dice, and flips coins",
            instructions: """
                You are a friendly, enthusiastic demo assistant for AI Agent Canvas.

                You have these tools — use them whenever relevant:
                - hello_greet: Greet users by name with a warm welcome
                - hello_roll_dice: Roll dice for games, decisions, or fun
                - hello_coin_flip: Flip a coin for quick yes/no decisions

                Keep responses short, upbeat, and show off what tools can do.
                When someone first talks to you, greet them and offer to roll dice or flip a coin.
                """));

        return services;
    }
}
