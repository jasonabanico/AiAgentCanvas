> [Developer Guide](developer-guide.md) > Adding Agents

# Developer Guide: Adding Agents

A custom agent in AI Agent Canvas is a self-contained project under `src/Agents/` that seeds all the components the agent needs to function: **persona**, **context**, **workflows**, **entities**, **user profiles**, **guardrails**, **goals**, **skills**, and **MCP connections**. Agents and data connections are separate projects: agents define *how* the LLM behaves, data connections define *what* it can do.

## How It Works

Data connections (like `MCP.HelloWorldData`) register tools as `IReadOnlyList<AITool>` services. Custom agents seed their components via seed interfaces (`IPersonaSeed`, `IContextSeed`, `IWorkflowSeed`, `IEntitySeed`, `IGuardrailSeed`, `IGoalSeed`, `ISkillSeed`, `IMcpConnectionSeed`) that the platform resolves at startup. Seeded data is saved to disk (or database) if it doesn't already exist, preserving any manual edits.

## Step-by-Step Guide

### Step 1: Create a Project Folder

Create a new folder under `src/Agents/`:

```
src/Agents/Agent.MyAgent/
```

### Step 2: Create the .csproj

A custom agent only needs two dependencies: `Abstractions` (for the seed interfaces) and `Microsoft.Extensions.DependencyInjection.Abstractions` (for the service extension). No `Microsoft.Extensions.AI` -- agents don't own tools.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.9" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\AiAgentCanvas\AiAgentCanvas.Abstractions\AiAgentCanvas.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

### Step 3: Create a Service Extension

Register seeds for the components your agent needs. At minimum, seed a persona. Optionally seed context, workflows, entities, guardrails, goals, skills, and MCP connections:

```csharp
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MyAgent;

public static class MyAgentServiceExtensions
{
    public static IServiceCollection AddMyAgent(this IServiceCollection services)
    {
        // Persona: how the LLM behaves
        services.AddSingleton<IPersonaSeed>(new PersonaSeed(
            name: "customer-support",
            description: "Customer support assistant for Contoso Electronics",
            instructions: """
                You are a customer support assistant for Contoso Electronics.
                Use search_kb and lookup_order to help customers.
                Always greet warmly and never share internal pricing.
                """));

        // Guardrail: policy constraint
        services.AddSingleton<IGuardrailSeed>(new GuardrailSeed(
            name: "no-internal-pricing",
            severity: "high",
            enabled: true,
            rule: "Never reveal wholesale costs, margins, or internal pricing to customers."));

        // Workflow: reusable multi-step task
        services.AddSingleton<IWorkflowSeed>(new WorkflowSeed(
            name: "order-investigation",
            description: "Investigate a customer order issue end-to-end",
            tags: "support,orders",
            content: """
                ## Order Investigation
                1. Look up the order with `lookup_order`
                2. Check product details with `search_kb`
                3. Summarize findings and recommend next steps
                """));

        // Context: background knowledge
        services.AddSingleton<IContextSeed>(new ContextSeed(
            topic: "contoso-return-policy",
            tags: "support,policy",
            type: "fact",
            content: "Contoso offers 30-day returns on all electronics..."));

        // Entity: domain schema
        services.AddSingleton<IEntitySeed>(new EntitySeed(
            name: "support-ticket",
            type: "ticket",
            tags: "support",
            content: "Schema: customer name, order number, issue description, resolution..."));

        // Skill: reusable prompt template
        services.AddSingleton<ISkillSeed>(new SkillSeed(
            name: "escalation-summary",
            description: "Generate an escalation summary for a support case",
            promptTemplate: "Summarize the support case for {{customer}} regarding order {{order}}..."));

        // Tool dependencies: validated at startup, warns if missing
        services.AddSingleton<IToolDependencySeed>(new ToolDependencySeed(
            agentName: "customer-support",
            requiredTools: ["search_kb", "lookup_order"]));

        return services;
    }
}
```

The tools referenced in the persona (`search_kb`, `lookup_order`) come from a separate data connection project. The `IToolDependencySeed` declares this dependency explicitly -- at startup the platform validates that all required tools are registered and logs warnings for any missing ones.

### Step 4: Add ProjectReference in AiAgentCanvas.Orchestrator.csproj

Open `src/Orchestrator/AiAgentCanvas.Orchestrator/AiAgentCanvas.Orchestrator.csproj` and add a reference to your project:

```xml
<ProjectReference Include="..\..\Agents\Agent.MyAgent\Agent.MyAgent.csproj" />
```

### Step 5: Add to AiAgentCanvas.sln

Add the project to the solution under the Agents solution folder:

```
dotnet sln AiAgentCanvas.sln add src/Agents/Agent.MyAgent/Agent.MyAgent.csproj --solution-folder Agents
```

### Step 6: Wire Up in Program.cs

```csharp
using MyAgent;

builder.Services.AddMyAgent();
```

The persona is saved to `agent-data/orchestrator/agent/personas/customer-support.md` on first startup. Users activate it with: *"switch to the customer-support persona"*.

## Full Reference: HelloWorldAgent

The `Agent.HelloWorld` in `src/Agents/Agent.HelloWorld/` is the built-in starter example. It demonstrates the complete custom agent pattern -- a financial analyst that seeds all component types and references tools from the `MCP.HelloWorldData` data connection.

### HelloWorldServiceExtensions.cs

The entire agent is a single file that seeds every component type:

| Seed | Name | Purpose |
|------|------|---------|
| `IPersonaSeed` | `financial-analyst` | Role instructions referencing market data tools |
| `IContextSeed` | `financial-analysis-methodology` | Reference material for P/E, revenue growth, EPS, etc. |
| `IWorkflowSeed` | `full-stock-analysis` | Quote -> history -> fundamentals -> summary |
| `IEntitySeed` | `stock-analysis-report` | Schema for structured analysis output |
| `IGuardrailSeed` | `investment-disclaimer` | Never provide buy/sell recommendations |
| `ISkillSeed` | `compare-stocks` | Prompt template for side-by-side stock comparison |
| `IToolDependencySeed` | `financial-analyst` | Declares dependency on stock_quote, stock_history, edgar_company_facts |

The tools (`stock_quote`, `stock_history`, `edgar_company_facts`) are registered by the `MCP.HelloWorldData` data connection in a separate project. The agent doesn't own or define any tools -- it only tells the LLM how to use them.

Wired in `Program.cs` with a single line: `builder.Services.AddHelloWorldAgent();`

## Component Seeding

Seed interfaces let custom agents ship their own data. At startup, the platform resolves all registered seed services and saves any that don't already exist on disk (or in the database for skills). This means:

- Components are created automatically on first run
- Manual edits to persisted files are preserved (seeds never overwrite)
- Users interact with seeded components the same way as manually created ones
- Seed data is written to the per-agent `agent-data/<agent>/agent/` directory

**No-code alternative:** Instead of implementing seed interfaces in code, you can drop hand-written markdown files into the `agent-data/<agent>/user/` directories. These files are read alongside system-created data but are never overwritten by the system. Use the same YAML frontmatter format shown in the [Agent Data](#agent-data) section.

| Interface | Concrete Class | Fields | Persisted To |
|-----------|---------------|--------|-------------|
| `IPersonaSeed` | `PersonaSeed` | name, description, instructions | `agent-data/<agent>/agent/personas/*.md` |
| `IContextSeed` | `ContextSeed` | topic, type, tags, content | `agent-data/<agent>/agent/context/*.md` |
| `IWorkflowSeed` | `WorkflowSeed` | name, description, tags, content | `agent-data/<agent>/agent/workflows/*.md` |
| `IEntitySeed` | `EntitySeed` | name, type, tags, content | `agent-data/<agent>/agent/entities/*.md` |
| `IGuardrailSeed` | `GuardrailSeed` | name, severity, enabled, rule | `agent-data/<agent>/agent/guardrails/*.md` |
| `IGoalSeed` | `GoalSeed` | name, description, priority, acceptanceCriteria, assignedAgent, content | `agent-data/<agent>/agent/goals/*.md` |
| `ISkillSeed` | `SkillSeed` | name, description, promptTemplate | `skills.db` (SQLite) |
| `IMcpConnectionSeed` | `McpConnectionSeed` | name, endpoint, transport | In-memory (connected at startup) |
| `IToolDependencySeed` | `ToolDependencySeed` | agentName, requiredTools | Validated at startup (warnings for missing tools) |

## Switching Between Agents

Users switch agents at runtime via personas -- no restart needed. Say *"switch to the hello-world persona"* or *"switch to the customer-support persona"*. Say *"switch to default"* to return to the base system prompt. See [Agent Data](#agent-data) for details.

## Testing Your Agent

1. Build the solution: `dotnet build AiAgentCanvas.sln`
2. Run the backend: `cd src/Orchestrator/AiAgentCanvas.Orchestrator && dotnet run`
3. Run the frontend: `cd frontend && npm run dev`
4. Open the CopilotKit UI and interact with your agent
5. Check the console logs for tool calls and governance events

---

