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
    private const string DefaultRoot = "./agent-data/orchestrator";
    private const string DefaultSharedRoot = "./agent-data/shared";

    private static string[] SharedDirs(string sharedRoot, string domain) =>
    [
        Path.Combine(sharedRoot, "agent", domain),
        Path.Combine(sharedRoot, "user", domain),
    ];

    public static IServiceCollection AddAiAgentCanvasPersonas(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new PersonaStore(
            Path.Combine(rootDirectory, "agent", "personas"),
            Path.Combine(rootDirectory, "user", "personas"),
            SharedDirs(sharedRootDirectory, "personas"));
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
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new ContextStore(
            Path.Combine(rootDirectory, "agent", "context"),
            Path.Combine(rootDirectory, "user", "context"),
            SharedDirs(sharedRootDirectory, "context"));
        services.AddSingleton(store);
        services.AddSingleton<ContextToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<ContextToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
        {
            foreach (var seed in sp.GetServices<IContextSeed>())
            {
                if (store.Get(seed.Topic) is null)
                    store.Save(seed.Topic, seed.Type, seed.Tags, seed.Content);
            }
            return new PersistentContextProvider(store);
        });
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasWorkflows(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new WorkflowStore(
            Path.Combine(rootDirectory, "agent", "workflows"),
            Path.Combine(rootDirectory, "user", "workflows"),
            SharedDirs(sharedRootDirectory, "workflows"));
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

    public static IServiceCollection AddAiAgentCanvasEntities(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new EntityStore(
            Path.Combine(rootDirectory, "agent", "entities"),
            Path.Combine(rootDirectory, "user", "entities"),
            SharedDirs(sharedRootDirectory, "entities"));
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
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new UserProfileStore(
            Path.Combine(rootDirectory, "agent", "profiles"),
            Path.Combine(rootDirectory, "user", "profiles"),
            SharedDirs(sharedRootDirectory, "profiles"));
        services.AddSingleton(store);
        services.AddSingleton<UserProfileToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<UserProfileToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
        {
            foreach (var seed in sp.GetServices<IUserProfileSeed>())
            {
                if (store.Get(seed.Name) is null)
                    store.Save(seed.Name, seed.Role, seed.Timezone, seed.Content);
            }
            return new UserProfileContextProvider(store);
        });
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasGuardrails(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new GuardrailStore(
            Path.Combine(rootDirectory, "agent", "guardrails"),
            Path.Combine(rootDirectory, "user", "guardrails"),
            SharedDirs(sharedRootDirectory, "guardrails"));
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

}
