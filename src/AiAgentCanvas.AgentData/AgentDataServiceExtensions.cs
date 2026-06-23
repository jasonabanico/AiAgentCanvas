using AiAgentCanvas.Abstractions;
using AiAgentCanvas.AgentData.Context;
using AiAgentCanvas.AgentData.Entities;
using AiAgentCanvas.AgentData.Goals;
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
    private const string DefaultRoot = "./agent-data";

    public static IServiceCollection AddAiAgentCanvasPersonas(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot)
    {
        var store = new PersonaStore(
            Path.Combine(rootDirectory, "agent", "personas"),
            Path.Combine(rootDirectory, "user", "personas"));
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
        string rootDirectory = DefaultRoot)
    {
        var store = new ContextStore(
            Path.Combine(rootDirectory, "agent", "context"),
            Path.Combine(rootDirectory, "user", "context"));
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
        string rootDirectory = DefaultRoot)
    {
        var store = new WorkflowStore(
            Path.Combine(rootDirectory, "agent", "workflows"),
            Path.Combine(rootDirectory, "user", "workflows"));
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
        string rootDirectory = DefaultRoot)
    {
        var store = new EntityStore(
            Path.Combine(rootDirectory, "agent", "entities"),
            Path.Combine(rootDirectory, "user", "entities"));
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
        string rootDirectory = DefaultRoot)
    {
        services.AddSingleton(new UserProfileStore(
            Path.Combine(rootDirectory, "agent", "profiles"),
            Path.Combine(rootDirectory, "user", "profiles")));
        services.AddSingleton<UserProfileToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<UserProfileToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
            new UserProfileContextProvider(sp.GetRequiredService<UserProfileStore>()));
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasGuardrails(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot)
    {
        var store = new GuardrailStore(
            Path.Combine(rootDirectory, "agent", "guardrails"),
            Path.Combine(rootDirectory, "user", "guardrails"));
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

    public static IServiceCollection AddAiAgentCanvasGoals(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot,
        string workQueueConnectionString = "Data Source=workqueue.db")
    {
        var goalStore = new GoalStore(
            Path.Combine(rootDirectory, "agent", "goals"),
            Path.Combine(rootDirectory, "user", "goals"));
        services.AddSingleton(goalStore);
        services.AddSingleton(new WorkQueueStore(workQueueConnectionString));
        services.AddSingleton<GoalToolProvider>();
        services.AddSingleton<WorkQueueToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        {
            foreach (var seed in sp.GetServices<IGoalSeed>())
            {
                if (goalStore.Get(seed.Name) is null)
                    goalStore.Save(seed.Name, seed.Description, seed.Priority, "active",
                        seed.AcceptanceCriteria, seed.AssignedAgent, seed.Content);
            }
            return sp.GetRequiredService<GoalToolProvider>().GetTools();
        });
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<WorkQueueToolProvider>().GetTools());
        return services;
    }
}
