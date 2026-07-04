> [Developer Guide](developer-guide.md) > Agent Data

# Developer Guide: Agent Data

The `AiAgentCanvas.AgentData` project provides seven markdown-persisted data domains. Each domain follows an identical three-class pattern and gives the LLM tools to manage its own configuration at runtime.

## Directory Layout

Agent data defaults to per-agent directories under a common `agent-data/` root. The orchestrator and each agent get their own subdirectory, with a `shared/` directory for cross-agent data. Each subdirectory contains `agent/` (system writes) and `user/` (hand-written, read-only to system) folders:

```
agent-data/
├── orchestrator/       <-- Orchestrator's own agent data
│   ├── agent/          <-- System writes here (seeds + tool-created content)
│   │   ├── personas/
│   │   ├── context/
│   │   ├── workflows/
│   │   ├── entities/
│   │   ├── profiles/
│   │   ├── guardrails/
│   │   └── goals/
│   └── user/           <-- Hand-written MD files (read-only to system, never overwritten)
│       ├── personas/
│       ├── context/
│       └── ...
└── shared/             <-- Shared data accessible to all agents
    ├── agent/
    │   ├── personas/
    │   ├── context/
    │   └── ...
    └── user/
        ├── personas/
        ├── context/
        └── ...
```

- **`agent/`** -- All system writes go here: seed data from custom agents and content created via chat tools (create_persona, save_context, etc.).
- **`user/`** -- A place for hand-written markdown files that the system reads but never overwrites. This is the no-code alternative to implementing seed interfaces -- simply drop a properly formatted markdown file into the appropriate subdirectory.
- **`shared/`** -- Data that is accessible to all agents. Use the `sharedRootDirectory` parameter when registering domains to enable shared data alongside per-agent data.

All directories are created automatically on startup. Each store's `ListAll()` method merges files from both the per-agent and shared directories, so the LLM sees user-provided, system-created, and shared data together.

## The Seven Domains

| Domain | Agent Path | User Path | Purpose |
|--------|-----------|-----------|---------|
| Personas | `agent/personas/` | `user/personas/` | Custom agent identities with instructions |
| Context | `agent/context/` | `user/context/` | Typed persistent context (fact, reference, decision, feedback) |
| Workflows | `agent/workflows/` | `user/workflows/` | Multi-step workflow definitions |
| Entities | `agent/entities/` | `user/entities/` | Domain entity schemas |
| UserProfiles | `agent/profiles/` | `user/profiles/` | User preference profiles |
| Guardrails | `agent/guardrails/` | `user/guardrails/` | Behavioral constraints |
| Goals | `agent/goals/` | `user/goals/` | Goals and objectives for autonomous execution |

## The Store / ToolProvider / ContextProvider Pattern

Every domain implements exactly three classes:

1. **Store** -- CRUD operations on markdown files in a directory
2. **ToolProvider** -- Exposes store operations as `AITool` instances the LLM can call
3. **AIContextProvider** (optional) -- Injects domain data into the system prompt before each LLM call

This pattern means the LLM can create, read, update, and delete domain data through tool calls, and that data automatically influences the LLM's behavior through context injection.

## MarkdownFile: The Persistence Foundation

All agent data is stored as markdown files with YAML frontmatter. The `MarkdownFile` utility class in `AiAgentCanvas.Abstractions` handles parsing and serialization.

### File Format

```markdown
---
name: code-reviewer
description: Reviews code for quality and correctness
---

You are an expert code reviewer. When reviewing code:
- Check for bugs, security issues, and performance problems
- Suggest improvements with specific code examples
- Be constructive and explain your reasoning
```

### Key Methods

```csharp
// Parse a single file
MarkdownFile? file = MarkdownFile.Parse("agent-data/personas/code-reviewer.md");
string name = file.Get("name");           // "code-reviewer"
string desc = file.Get("description");    // "Reviews code for quality..."
string body = file.Body;                  // The instruction text

// Write a file (creates directory if needed)
MarkdownFile.Write(
    "agent-data/personas/code-reviewer.md",
    new Dictionary<string, string>
    {
        ["name"] = "code-reviewer",
        ["description"] = "Reviews code for quality and correctness",
    },
    "You are an expert code reviewer...");

// Load all files from a directory
List<MarkdownFile> all = MarkdownFile.LoadAll("agent-data/personas/");

// Sanitize a name for use as a filename
string safe = MarkdownFile.SanitizeFileName("Code Reviewer"); // "code-reviewer"
```

## Detailed Walkthrough: Personas

Personas are the most illustrative domain. Here is how all three classes work together.

### PersonaStore

The store manages CRUD operations on persona markdown files and tracks which persona is active via a `.active` marker file.

```csharp
public sealed class PersonaStore
{
    private readonly string _directory;        // agent/ -- system writes here
    private readonly string _userDirectory;    // user/ -- read-only, never overwritten
    private readonly string[] _readDirectories;

    public PersonaStore(string directory, string userDirectory)
    {
        // Creates both directories if missing
    }

    public void SavePersona(string name, string description, string instructions)
    {
        // Always writes to _directory (agent/)
        MarkdownFile.Write(
            Path.Combine(_directory, MarkdownFile.SanitizeFileName(name) + ".md"),
            new Dictionary<string, string>
            {
                ["name"] = name,
                ["description"] = description,
            },
            instructions);
    }

    public List<PersonaInfo> ListPersonas()
    {
        // Merges files from both agent/ and user/ directories
        return _readDirectories
            .SelectMany(dir => MarkdownFile.LoadAll(dir))
            .Select(ToPersona)
            .Where(p => p is not null)
            .Cast<PersonaInfo>()
            .ToList();
    }

    public PersonaInfo? GetPersona(string name) { ... }
    public bool DeletePersona(string name) { ... }
    public string? GetActivePersonaName() { ... }
    public void SetActivePersona(string? name) { ... }
    public string? GetActiveInstructions() { ... }
}
```

### PersonaToolProvider

The tool provider creates `AITool` instances using `AIFunctionFactory.Create()`. Each tool delegates to the store.

```csharp
public sealed class PersonaToolProvider
{
    private readonly PersonaStore _store;

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(CreatePersona, "create_persona",
                "Create a new persona with custom instructions"),
            AIFunctionFactory.Create(ListPersonas, "list_personas",
                "List all available personas"),
            AIFunctionFactory.Create(SwitchPersona, "switch_persona",
                "Switch to a different persona"),
            AIFunctionFactory.Create(ReadPersona, "read_persona",
                "Read the full details of a persona"),
            AIFunctionFactory.Create(UpdatePersona, "update_persona",
                "Update an existing persona"),
            AIFunctionFactory.Create(DeletePersona, "delete_persona",
                "Delete a persona"),
        ];
    }

    private string CreatePersona(string name, string description, string instructions)
    {
        var existing = _store.GetPersona(name);
        if (existing is not null)
            return JsonSerializer.Serialize(new { error = "Persona already exists..." });

        _store.SavePersona(name, description, instructions);
        return JsonSerializer.Serialize(new { status = "created", name });
    }

    // ... similar implementations for other tools
}
```

### PersonaContextProvider

The context provider reads the active persona and appends its instructions to the system prompt before each LLM call.

```csharp
internal sealed class PersonaContextProvider : AIContextProvider
{
    private readonly PersonaStore _store;
    private readonly string _defaultPrompt;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var activeInstructions = _store.GetActiveInstructions();
        if (!string.IsNullOrEmpty(activeInstructions))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") + "\n" + activeInstructions;
        }
        else if (string.IsNullOrEmpty(context.AIContext.Instructions))
        {
            context.AIContext.Instructions = _defaultPrompt;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

## Registration

The `AgentDataServiceExtensions` class provides one `Add*()` method per domain. Each method registers the Store, ToolProvider, tools, and (where applicable) the AIContextProvider.

```csharp
private const string DefaultRoot = "./agent-data/orchestrator";
private const string DefaultSharedRoot = "./agent-data/shared";

public static IServiceCollection AddAiAgentCanvasPersonas(
    this IServiceCollection services,
    string rootDirectory = DefaultRoot,
    string? sharedRootDirectory = DefaultSharedRoot)
{
    var store = new PersonaStore(
        Path.Combine(rootDirectory, "agent", "personas"),   // system writes
        Path.Combine(rootDirectory, "user", "personas"),    // user-provided (read-only)
        sharedRootDirectory is not null
            ? Path.Combine(sharedRootDirectory, "agent", "personas")
            : null);                                        // shared data (optional)
    services.AddSingleton(store);
    services.AddSingleton<PersonaToolProvider>();
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<PersonaToolProvider>().GetTools());
    services.AddSingleton<AIContextProvider>(sp =>
    {
        // Seed data from custom agents is written to agent/ directory
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
```

All seven domains are registered individually in `Program.cs`:

```csharp
builder.Services.AddAiAgentCanvasPersonas();
builder.Services.AddAiAgentCanvasContext();
builder.Services.AddAiAgentCanvasWorkflows();
builder.Services.AddAiAgentCanvasEntities();
builder.Services.AddAiAgentCanvasUserProfiles();
builder.Services.AddAiAgentCanvasGuardrails();
builder.Services.AddAiAgentCanvasGoals();
```

## Other Domains

Each domain follows the same pattern as Personas but with domain-specific data:

| Domain | Context Provider | What it injects |
|--------|-----------------|-----------------|
| Personas | `PersonaContextProvider` | Active persona instructions |
| Context | `PersistentContextProvider` | All persistent context entries, grouped by type (fact, reference, decision, feedback) |
| Guardrails | `GuardrailContextProvider` | Active guardrail constraints |
| Entities | `EntityContextProvider` | Entity schemas and definitions |
| UserProfiles | `UserProfileContextProvider` | Active user preferences |
| Workflows | *(none)* | Executed on demand via `WorkflowExecutor` |
| Goals | *(none)* | Goals are managed via tools only; used by the autonomous execution job |

## Context Types

Context entries support an optional `type` field in their YAML frontmatter that categorizes the knowledge. When injected into the system prompt, entries are grouped by type for clarity.

| Type | Purpose | Example |
|------|---------|---------|
| `fact` | Domain knowledge -- things that are true | "Our fiscal year starts in April", "The main database is Postgres 16" |
| `reference` | Pointers to external systems and resources | "Bug tracker is at linear.app/project-X", "Oncall dashboard at grafana.internal/api-latency" |
| `decision` | Past choices with rationale | "Chose GraphQL over REST because clients need flexible queries" |
| `feedback` | Learned behavioral adjustments | "User prefers tables over bullet points", "Always include source citations" |

The type field is free-form -- you can use any string, not just the four conventions above. Entries without a type are grouped as "General". When injected into the system prompt, entries are grouped by their type label.

### File Format

```markdown
---
topic: fiscal-year-schedule
type: fact
tags: finance,calendar
---
Our fiscal year runs April 1 through March 31.
Q1: Apr-Jun, Q2: Jul-Sep, Q3: Oct-Dec, Q4: Jan-Mar.
```

## Creating a New Domain

To add a new agent data domain, follow the same pattern:

1. Create a subdirectory under `AiAgentCanvas.AgentData/` (e.g., `Templates/`)
2. Create a `TemplateStore` class with CRUD methods using `MarkdownFile`
3. Create a `TemplateToolProvider` class exposing tools via `AIFunctionFactory.Create()`
4. Optionally create a `TemplateContextProvider` extending `AIContextProvider`
5. Add an `AddAiAgentCanvasTemplates()` method to `AgentDataServiceExtensions`
6. Call the new method in `Program.cs`

---

