> [Developer Guide](developer-guide.md) > Behavior Patterns

# Developer Guide: Behavior Patterns

## Overview {#behavior-patterns-overview}

Agent behavior patterns describe how an agent reasons, acts, and interacts with other agents. The platform does not impose a pattern -- the pattern emerges from how you write the agent's persona instructions, which tools you expose, and how you wire up inter-agent communication.

The same platform primitives (personas, tools, handoffs, mailboxes) support all of these patterns. You choose the pattern when designing the agent, not when configuring the platform.

| Pattern | Key Mechanism | When to Use |
|---------|---------------|-------------|
| Sequential | Persona instructions with ordered steps | Predictable multi-step workflows |
| Reactive (ReAct) | Tool loop (built-in) | Most common -- agent reasons about what tool to call next |
| Parallel | Multiple agents via handoff or messaging | Independent subtasks that can run concurrently |
| Deliberate | PlannerContextProvider | Complex tasks that benefit from upfront planning |
| Reflective | Agent calls itself or a critic agent | Tasks where output quality matters more than speed |
| Hierarchical | Agent handoffs with delegation | Manager/worker decomposition |
| Collaborative | Multi-agent messaging | Tasks that benefit from multiple perspectives |

---

## Sequential

The agent follows a fixed sequence of steps defined in its persona instructions. Each step runs to completion before the next begins. The LLM does not decide the order -- the instructions dictate it.

**When to use:** ETL pipelines, report generation, onboarding checklists -- any workflow where the steps are known upfront and don't change based on intermediate results.

**How to implement:** Define the steps in the persona's `Instructions` field. The agent's tool loop will follow them in order.

```markdown
# Persona: daily-report-generator

## Instructions
You generate a daily market report. Follow these steps in order:

1. Use `stock_quote` to get the current price for AAPL, MSFT, and GOOGL
2. Use `stock_history` to get the 5-day price history for each
3. Use `edgar_company_facts` to get the latest quarterly revenue for each
4. Write a summary report with:
   - Current prices and daily change
   - 5-day trend (up/down/flat)
   - Most recent quarterly revenue
   - Any notable movements (>2% change)

Do not skip steps. Do not reorder steps.
```

```csharp
// Registration -- persona instructions drive the sequential behavior
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "daily-report-generator",
    description: "Generates daily market summary reports",
    instructions: File.ReadAllText("agent-data/personas/daily-report-generator.md")));

// Restrict to only the tools needed for the sequence
services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "daily-report-generator",
    toolNames: ["stock_quote", "stock_history", "edgar_company_facts"]));
```

**What makes it sequential:** The persona instructions say "follow these steps in order" and "do not reorder." The LLM complies because the instructions are explicit. No platform feature enforces the ordering -- it's a prompting pattern.

---

## Reactive (ReAct)

The agent observes the current state (conversation history, tool results) and decides what to do next. This is the default behavior of any agent on the platform -- the MAF tool loop already implements reason-then-act cycles.

**When to use:** Most agents. Any task where the next action depends on what the agent learned from the previous action.

**How to implement:** Write persona instructions that describe the agent's goal and available tools, but don't prescribe a fixed order. Let the LLM decide what to call and when to stop.

```markdown
# Persona: stock-analyst

## Instructions
You are a stock analyst. When the user asks about a stock:

- Use available tools to gather data as needed
- If the quote looks unusual (>3% daily change), dig deeper with history and fundamentals
- If the user asks "why," look at recent SEC filings
- Stop when you have enough information to give a clear answer

Think about what data you need before making each tool call.
```

```csharp
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "stock-analyst",
    description: "Analyzes stocks reactively based on user questions",
    instructions: File.ReadAllText("agent-data/personas/stock-analyst.md")));

services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "stock-analyst",
    toolNames: ["stock_quote", "stock_history", "edgar_company_facts"]));
```

**What makes it reactive:** The agent decides what to do based on what it sees. If the quote shows a big move, it digs deeper. If the user asks "why," it checks filings. The persona instructions describe conditions and responses, not a fixed sequence.

---

## Parallel (Fan-out / Fan-in)

Multiple agents work on independent subtasks at the same time. Results are collected and merged. This pattern requires inter-agent communication.

**When to use:** When the task decomposes into independent parts (e.g., analyzing multiple stocks, checking multiple data sources, running multiple validations).

**How to implement:** Use `IAgentMessaging` to send tasks to multiple agents and collect results. Or use the handoff system to delegate to specialists.

```markdown
# Persona: portfolio-analyzer

## Instructions
You analyze a portfolio of stocks. When the user provides a list of tickers:

1. Use `handoff` to send each ticker to the "stock-analyst" agent for individual analysis
2. Wait for all analyses to come back
3. Synthesize a portfolio-level summary:
   - Overall portfolio direction
   - Best and worst performers
   - Any correlated movements
   - Risk flags
```

```csharp
// The coordinator agent
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "portfolio-analyzer",
    description: "Coordinates parallel analysis of multiple stocks",
    instructions: File.ReadAllText("agent-data/personas/portfolio-analyzer.md")));

// It needs the handoff tool to delegate to other agents
services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "portfolio-analyzer",
    toolNames: ["handoff_to_agent", "list_agents"]));

// The worker agent (stock-analyst) is registered separately
// and handles individual stock analysis
```

**What makes it parallel:** The coordinator agent delegates independent work items to worker agents. Each worker runs its own tool loop independently. The coordinator waits for results and synthesizes them.

---

## Deliberate (Planning)

The agent creates an explicit plan before taking action. The platform's `PlannerContextProvider` injects planning context, and the agent can use workflow definitions to structure its approach.

**When to use:** Complex, multi-step tasks where mistakes are costly. Tasks that benefit from the agent thinking through its approach before starting.

**How to implement:** Use the workflow definitions in `agent-data/workflows/` to give the agent a plan template, or instruct the agent to create a plan first.

```markdown
# Persona: migration-planner

## Instructions
You plan and execute database migrations. Before taking any action:

1. **Plan phase:** List all the changes needed, their dependencies, and the order
   they must run in. Present the plan to the user and wait for approval.
2. **Validate phase:** For each planned change, verify preconditions
   (table exists, column type is compatible, no data loss).
3. **Execute phase:** Run the changes in the planned order.
4. **Verify phase:** Confirm each change was applied correctly.

Never skip the planning phase. Never execute without user approval of the plan.
```

```markdown
# Workflow: database-migration (agent-data/workflows/database-migration.md)

---
name: database-migration
trigger: "migrate database"
---

## Steps
1. Analyze current schema and target schema
2. Generate migration plan with rollback steps
3. Present plan for approval
4. Execute migrations in dependency order
5. Run verification queries
6. Report results
```

**What makes it deliberate:** The agent explicitly creates a plan, presents it for review, and follows it. The `PlannerContextProvider` can inject workflow definitions into the agent's context so it has structured templates to work from.

---

## Reflective (Self-Critique)

The agent reviews its own output and iterates to improve quality. This can be done within a single agent (re-read and revise) or by handing off to a separate critic agent.

**When to use:** Content generation, code review, analysis where accuracy matters more than speed.

**How to implement:** Either instruct the agent to self-review, or create a dedicated critic agent and use handoffs.

```markdown
# Persona: report-writer

## Instructions
You write investment analysis reports. After drafting a report:

1. Re-read your draft critically
2. Check: Are all claims supported by data from tool calls?
3. Check: Are there any contradictions?
4. Check: Is anything missing that the user asked for?
5. If you find issues, revise and check again
6. Only present the final version to the user
```

For a separate critic agent:

```markdown
# Persona: report-critic

## Instructions
You review investment reports for quality. When you receive a report:

- Flag any claims not supported by cited data
- Flag any contradictions between sections
- Flag any missing analysis the user requested
- Rate overall quality: pass / needs revision
- If needs revision, list specific changes needed
```

```csharp
// Writer agent hands off to critic, gets feedback, revises
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "report-writer",
    description: "Writes investment reports with self-revision",
    instructions: File.ReadAllText("agent-data/personas/report-writer.md")));

services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "report-critic",
    description: "Reviews reports for accuracy and completeness",
    instructions: File.ReadAllText("agent-data/personas/report-critic.md")));

// Critic only needs the handoff tool to send feedback back
services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "report-critic",
    toolNames: ["handoff_to_agent"]));
```

**What makes it reflective:** The agent evaluates its own output against quality criteria before presenting it. With a critic agent, the evaluation is independent and less prone to self-confirmation bias.

---

## Hierarchical (Delegation)

A manager agent decomposes work and delegates to specialist agents. The manager decides which agent handles each part, collects results, and produces the final output.

**When to use:** When you have specialized agents that each own a domain, and tasks that span multiple domains.

**How to implement:** Use `handoff_to_agent` and `list_agents` tools. The manager persona describes delegation rules.

```markdown
# Persona: research-manager

## Instructions
You are a research manager. When the user asks a complex question:

1. Use `list_agents` to see available specialists
2. Break the question into sub-questions that match specialist domains
3. Use `handoff_to_agent` to delegate each sub-question to the right specialist
4. Collect results from all specialists
5. Synthesize a unified answer, citing which specialist provided which insight

Delegation rules:
- Financial data questions -> "stock-analyst"
- Regulatory/compliance questions -> "compliance-analyst"
- Market trend questions -> "market-researcher"
- If no specialist fits, handle the question yourself
```

```csharp
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "research-manager",
    description: "Decomposes research questions and delegates to specialists",
    instructions: File.ReadAllText("agent-data/personas/research-manager.md")));

// Manager needs agent registry tools plus its own research tools
services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "research-manager",
    toolNames: ["handoff_to_agent", "list_agents", "send_message", "read_messages"]));
```

**What makes it hierarchical:** The manager agent sits above the specialists and controls the flow. Specialists don't talk to each other -- they report back to the manager, who owns the final synthesis.

---

## Collaborative (Multi-Agent Debate)

Multiple agents with different perspectives analyze the same problem and debate or compare their conclusions. Useful for reducing bias and catching blind spots.

**When to use:** Risk assessment, code review, investment decisions -- any task where a single perspective might miss something.

**How to implement:** Use `AgentMailbox` for asynchronous message exchange between agents. Each agent reads the others' positions and responds.

```markdown
# Persona: bull-analyst

## Instructions
You are a bull-case analyst. Your job is to argue the optimistic case for a stock.
When analyzing:
- Focus on growth catalysts, competitive advantages, and upside scenarios
- Read messages from other analysts and respond to their bearish arguments
- Acknowledge legitimate risks but explain why the bull case still holds
- Be specific with data from tool calls, not generic optimism
```

```markdown
# Persona: bear-analyst

## Instructions
You are a bear-case analyst. Your job is to argue the cautious case for a stock.
When analyzing:
- Focus on risks, valuation concerns, and downside scenarios
- Read messages from other analysts and respond to their bullish arguments
- Acknowledge legitimate strengths but explain why the risks dominate
- Be specific with data from tool calls, not generic pessimism
```

```csharp
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "bull-analyst",
    description: "Argues the optimistic case for investments",
    instructions: File.ReadAllText("agent-data/personas/bull-analyst.md")));

services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "bear-analyst",
    description: "Argues the cautious case for investments",
    instructions: File.ReadAllText("agent-data/personas/bear-analyst.md")));

// Both analysts need market data tools plus messaging
services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "bull-analyst",
    toolNames: ["stock_quote", "stock_history", "edgar_company_facts",
                "send_message", "read_messages"]));

services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "bear-analyst",
    toolNames: ["stock_quote", "stock_history", "edgar_company_facts",
                "send_message", "read_messages"]));
```

**What makes it collaborative:** Multiple agents analyze the same data independently, then read each other's conclusions and respond. The user gets a balanced view instead of a single agent's perspective.

---

## Tool Scoping

All patterns above use `IAgentToolsSeed` to restrict which tools each agent can see. This is important for:

- **Focus:** An agent with 3 tools makes better decisions than one with 30
- **Safety:** A report-writing agent shouldn't have access to `delete_file`
- **Cost:** Fewer tools in the prompt means fewer tokens per request

```csharp
// Register the tool scope for an agent
services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "my-agent",
    toolNames: ["tool_a", "tool_b"]));
```

**Behavior:**
- If an agent has an `IAgentToolsSeed` registered, it only sees the listed tools
- If an agent has no `IAgentToolsSeed` registered, it sees all tools (default)
- If a listed tool is not registered in the system, a warning is logged at startup

This applies to both the default agent (configured via `AddAiAgentCanvas`) and persona-based agents (built by `AgentRegistry`).

---

## Choosing a Pattern

```
Is the order of steps known upfront?
├── Yes, always the same -> Sequential
└── No, depends on context
    ├── Single agent deciding its own actions -> Reactive (ReAct)
    ├── Should it plan before acting? -> Deliberate
    ├── Should it check its own work? -> Reflective
    ├── Multiple independent subtasks?
    │   ├── Same type of work, different inputs -> Parallel
    │   └── Different specialties needed -> Hierarchical
    └── Need multiple perspectives on the same question? -> Collaborative
```

Patterns can be combined. A hierarchical manager might use deliberate planning to decompose work, delegate to parallel workers that each use reactive tool loops, and then run a reflective quality check before presenting results.

---

> **[Download the complete PDF guide](guides/AI-Agent-Canvas-Guide.pdf)** | **[AI-First Company Guide](guides/AI-First-Company-Guide.pdf)**
