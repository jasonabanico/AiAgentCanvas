> [AI Agents](ai-agents.md) > AI Agents: Context Providers

# AI Agents: Context Providers

Context providers are the mechanism by which AI Agent Canvas shapes agent behavior **without modifying the agent itself**. They inject instructions, domain knowledge, guardrails, and user-specific information into the agent's context before every LLM call.

## What Are Context Providers

A context provider is a subclass of `AIContextProvider` from the Microsoft Agent Framework (`Microsoft.Agents.AI`). Each provider implements a single method that can modify the `AIContext` -- adding to the system instructions, attaching tools, or injecting retrieved documents:

```csharp
public abstract class AIContextProvider
{
    protected abstract ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken);
}
```

The `InvokingContext` gives the provider access to:

- **`AIContext`** -- The current context being built (instructions, tools, messages)
- **Session state** -- Via the agent session's `StateBag`

Providers return the modified `AIContext`, which flows to the next provider in the chain.

## The Context Injection Chain

Context providers execute in **registration order** before every LLM call. Each provider layers its contribution on top of what previous providers set:

```
LLM Call Request
     |
     v
+------------------------+
| SystemPromptProvider   |  --> Sets base system instructions
+------------------------+
     |
     v
+------------------------+
| PlannerContext...      |  --> Decomposes complex requests into step plans
+------------------------+
     |
     v
+------------------------+
| PersonaContextProvider |  --> Appends persona-specific instructions
+------------------------+
     |
     v
+------------------------+
| UserProfileContext...  |  --> Appends user preferences and profile data
+------------------------+
     |
     v
+------------------------+
| EntityContextProvider  |  --> Appends entity/domain knowledge index
+------------------------+
     |
     v
+------------------------+
| GuardrailContext...    |  --> Appends behavioral rules and constraints
+------------------------+
     |
     v
+------------------------+
| RagContextProvider     |  --> Appends retrieved documents from vector search
+------------------------+
     |
     v
+------------------------+
| GovernanceContext...   |  --> Scans for prompt injection, audits
+------------------------+
     |
     v
+------------------------+
| DynamicToolContext...  |  --> Attaches runtime tools from DynamicToolRegistry
+------------------------+
     |
     v
  Final AIContext sent to LLM
```

This layered approach means each provider is **independent and composable**. Adding a new provider does not require changes to existing ones.

## Built-in Context Providers

### SystemPromptProvider

The foundation of the context chain. Sets the base system instructions if none are already set:

```csharp
internal sealed class SystemPromptProvider : AIContextProvider
{
    private readonly string _systemPrompt;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(context.AIContext.Instructions))
            context.AIContext.Instructions = _systemPrompt;
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

The default prompt is configurable via `AiAgentCanvasOptions.SystemPrompt` or defaults to a generic helpful assistant prompt.

### PlanningMiddleware (Goal Decomposition)

Provides **persistent goal decomposition** for complex multi-step requests. Implemented as agent middleware (not a context provider) so it has access to the session's `StateBag` for plan persistence across messages.

On each user message, the middleware follows this logic:

1. **No existing plan:** Makes a lightweight LLM call (MaxOutputTokens=300, Temperature=0) to decide whether the request needs a multi-step plan. Simple requests (1-2 tool calls) get `NO_PLAN` and pass through unchanged. Complex requests get a numbered execution plan generated and stored in `StateBag`.
2. **Existing plan found:** Makes a continuation call that examines the conversation history to determine which steps are complete. Returns `COMPLETED: 1, 2 / REMAINING: 3, 4 / NEXT: 3` to track progress, `ALL_DONE` to clear the plan, or `REPLAN` if the user changed direction.

```csharp
public sealed class PlanningMiddleware
{
    public async Task InvokeAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? runOptions,
        Func<...> nextAsync,
        CancellationToken ct)
    {
        // Check StateBag for existing plan
        session?.StateBag.TryGetValue<string>("planner:active_plan", out var existingPlan);

        if (existingPlan is not null)
            planToInject = await EvaluateContinuationAsync(existingPlan, messages, ct);
        else
            planToInject = await GenerateNewPlanAsync(lastUserMessage, ct);

        // Persist plan to StateBag for next message
        session?.StateBag.SetValue("planner:active_plan", planToInject);

        // Inject plan into messages as system message
        messageList.Insert(0, new ChatMessage(ChatRole.System, plan));
        await nextAsync(messageList, session, runOptions, ct);
    }
}
```

The planner is aware of all registered tools (both static startup tools and dynamic MCP tools) and references them by name in the generated plan. The plan persists across messages via `StateBag`, enabling multi-turn workflows where the agent picks up where it left off. If the user changes direction mid-plan, the continuation evaluator detects this and generates a fresh plan.

### PersonaContextProvider

Appends persona-specific instructions from the `PersonaStore`. Personas define **how** the agent should behave -- tone, expertise level, domain focus:

```csharp
internal sealed class PersonaContextProvider : AIContextProvider
{
    private readonly PersonaStore _store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var activeInstructions = _store.GetActiveInstructions();
        if (!string.IsNullOrEmpty(activeInstructions))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") + "\n" + activeInstructions;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

Example persona instructions might be: "You are a financial analyst specializing in equity markets. Use precise numbers and cite data sources. Respond in a professional, concise tone."

### UserProfileContextProvider

Injects user-specific preferences and information:

```csharp
internal sealed class UserProfileContextProvider : AIContextProvider
{
    private readonly UserProfileStore _store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var profileContext = _store.LoadActiveProfileContext();
        if (!string.IsNullOrEmpty(profileContext))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") + profileContext;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

This enables personalization without the user repeating preferences. The profile might include role, team, preferred output format, or domain-specific terminology.

### EntityContextProvider

Appends a domain knowledge index from the `EntityStore`. Entities represent key concepts, terms, or reference data the agent should know about:

```csharp
internal sealed class EntityContextProvider : AIContextProvider
{
    private readonly EntityStore _store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var index = _store.LoadEntityIndex();
        if (!string.IsNullOrEmpty(index))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") + index;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

For example, an entity index might define company-specific product names, internal acronyms, or organizational structure that the LLM would not know from training data.

### GuardrailContextProvider

Appends behavioral constraints and rules:

```csharp
internal sealed class GuardrailContextProvider : AIContextProvider
{
    private readonly GuardrailStore _store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var rules = _store.LoadActiveRules();
        if (!string.IsNullOrEmpty(rules))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") + rules;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

Guardrail rules might include: "Never provide specific investment advice. Always include disclaimers when discussing financial data. Do not share PII across user sessions."

### RagContextProvider

The most sophisticated provider in the chain. It retrieves relevant documents from a vector store using a multi-stage pipeline:

1. **Hybrid search** -- combines vector cosine similarity (70%) with FTS5 keyword/BM25 scoring (30%) via the `IHybridSearchable` interface. Falls back to vector-only search if the store doesn't support hybrid.
2. **Metadata filtering** -- optionally filters by `Source` (exact match) and `Tags` (contains match) before scoring, reducing the candidate set.
3. **LLM reranking** -- sends the top-10 candidates to the LLM to re-score by relevance, then keeps the top-3. Falls back to original ranking on failure.
4. **Citation formatting** -- each result is numbered with its source and score, and the LLM is instructed to cite by number.

```csharp
public sealed class RagContextProvider : AIContextProvider
{
    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        // 1. Embed the user's query
        var queryEmbedding = await _embeddingGenerator
            .GenerateVectorAsync(lastUserMessage, cancellationToken: cancellationToken);

        // 2. Hybrid search (vector + keyword) with metadata filters
        if (_collection is IHybridSearchable hybridStore)
            await foreach (var result in hybridStore.HybridSearchAsync(...))
                candidates.Add(result);

        // 3. LLM reranking: top-10 -> top-3
        var results = _reranker is not null
            ? await _reranker.RerankAsync(query, candidates, topK)
            : candidates.Take(topK).ToList();

        // 4. Format with citations: [1] (source: X, score: 0.82)
        context.AIContext.Instructions +=
            "Relevant context (cite by number):\n" + ragContext;
    }
}
```

RAG differs from other providers in that it is **query-dependent** -- it reads the last user message, generates an embedding, and searches a vector store for relevant content.

### GovernanceContextProvider

Scans the assembled instructions for prompt injection attempts:

```csharp
public sealed class GovernanceContextProvider : AIContextProvider
{
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var result = _kernel.InjectionDetector.Detect(context.AIContext.Instructions);
        if (result.IsInjection)
        {
            _logger.LogWarning(
                "[GOVERNANCE] Prompt injection detected: {Type}",
                result.InjectionType);

            _kernel.AuditEmitter.Emit(
                GovernanceEventType.PolicyViolation, ...);
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

This provider typically runs **last** in the chain so it can scan the fully assembled instructions.

### DynamicToolContextProvider

Injects runtime tools from the `DynamicToolRegistry`. Unlike other providers that modify instructions, this one adds **tools**:

```csharp
internal sealed class DynamicToolContextProvider : AIContextProvider
{
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var dynamicTools = _registry.GetAllTools();
        if (dynamicTools.Count > 0)
        {
            var existing = context.AIContext.Tools?.ToList() ?? [];
            existing.AddRange(dynamicTools);
            context.AIContext.Tools = existing;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

## Creating Custom Context Providers

To add a custom context provider, subclass `AIContextProvider` and register it in DI:

```csharp
public sealed class CompanyKnowledgeProvider : AIContextProvider
{
    private readonly IKnowledgeBase _kb;

    public CompanyKnowledgeProvider(IKnowledgeBase kb) => _kb = kb;

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var knowledge = await _kb.GetRelevantFacts(cancellationToken);
        if (!string.IsNullOrEmpty(knowledge))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") +
                $"\n\nCompany knowledge:\n{knowledge}";
        }
        return context.AIContext;
    }
}
```

Register it in `Program.cs`:

```csharp
services.AddSingleton<AIContextProvider, CompanyKnowledgeProvider>();
```

The provider will execute as part of the chain, in the order it was registered.

## Design Guidelines

1. **Single responsibility** -- Each provider should handle one concern (persona, guardrails, RAG, etc.)
2. **Append, don't replace** -- Append to existing instructions rather than overwriting them, unless you are the system prompt provider
3. **Fail gracefully** -- If a provider's data source is unavailable, return the context unchanged rather than throwing
4. **Keep instructions concise** -- Every token in the instructions counts against the context window and adds cost
5. **Order matters** -- Register providers in logical order: base prompt first, then layered context, then guardrails, then governance scanning last

---

