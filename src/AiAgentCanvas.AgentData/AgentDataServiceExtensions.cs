using AiAgentCanvas.Abstractions;
using AiAgentCanvas.AgentData.Context;
using AiAgentCanvas.AgentData.Entities;
using AiAgentCanvas.AgentData.Guardrails;
using AiAgentCanvas.AgentData.Personas;
using AiAgentCanvas.AgentData.Profiles;
using AiAgentCanvas.AgentData.Workflows;
using AiAgentCanvas.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.AgentData;

public static class AgentDataServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasPersonas(
        this IServiceCollection services,
        string directory = "./agent-data/personas")
    {
        var store = new PersonaStore(directory);
        services.AddSingleton(store);
        services.AddSingleton<PersonaToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<PersonaToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
        {
            // Apply persona seeds from custom agents
            foreach (var seed in sp.GetServices<IPersonaSeed>())
            {
                if (store.GetPersona(seed.Name) is null)
                    store.SavePersona(seed.Name, seed.Description, seed.Instructions);
            }

            var defaultPrompt = sp.GetRequiredService<DefaultSystemPrompt>().Value;
            return new PersonaContextProvider(store, defaultPrompt);
        });
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasContext(
        this IServiceCollection services,
        string directory = "./agent-data/context")
    {
        var store = new ContextStore(directory);
        services.AddSingleton(store);
        services.AddSingleton<ContextToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<ContextToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
        {
            foreach (var seed in sp.GetServices<IContextSeed>())
            {
                if (store.Get(seed.Topic) is null)
                    store.Save(seed.Topic, seed.Tags, seed.Content);
            }
            return new PersistentContextProvider(store);
        });
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasWorkflows(
        this IServiceCollection services,
        string directory = "./agent-data/workflows")
    {
        var store = new WorkflowStore(directory);
        services.AddSingleton(store);
        services.AddSingleton<WorkflowExecutor>();
        services.AddSingleton<WorkflowToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        {
            foreach (var seed in sp.GetServices<IWorkflowSeed>())
            {
                if (store.Get(seed.Name) is null)
                    store.Save(seed.Name, seed.Description, seed.Tags, seed.Content);
            }
            return sp.GetRequiredService<WorkflowToolProvider>().GetTools();
        });
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasGuardrails(
        this IServiceCollection services,
        string directory = "./agent-data/guardrails")
    {
        var store = new GuardrailStore(directory);
        services.AddSingleton(store);
        services.AddSingleton<GuardrailToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<GuardrailToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
        {
            foreach (var seed in sp.GetServices<IGuardrailSeed>())
            {
                if (store.Get(seed.Name) is null)
                    store.Save(seed.Name, seed.Severity, seed.Enabled, seed.Rule);
            }
            return new GuardrailContextProvider(store);
        });
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasEntities(
        this IServiceCollection services,
        string directory = "./agent-data/entities")
    {
        var store = new EntityStore(directory);
        services.AddSingleton(store);
        services.AddSingleton<EntityToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<EntityToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
        {
            foreach (var seed in sp.GetServices<IEntitySeed>())
            {
                if (store.Get(seed.Name) is null)
                    store.Save(seed.Name, seed.Type, seed.Tags, seed.Content);
            }
            return new EntityContextProvider(store);
        });
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasUserProfiles(
        this IServiceCollection services,
        string directory = "./agent-data/profiles")
    {
        services.AddSingleton(new UserProfileStore(directory));
        services.AddSingleton<UserProfileToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<UserProfileToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
            new UserProfileContextProvider(sp.GetRequiredService<UserProfileStore>()));
        return services;
    }
}
