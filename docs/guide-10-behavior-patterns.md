# 10. Agent Behavior Patterns

Agent behavior patterns describe how an agent reasons, decides, and interacts. The platform does not enforce a pattern -- the pattern emerges from how you write the persona instructions, which tools you expose, and how you wire up inter-agent communication. The same platform primitives support all seven patterns.

## Overview

| Pattern | Key Mechanism | When to Use |
|---|---|---|
| Sequential | Persona instructions with ordered steps | Linear analysis pipelines, ETL, report generation |
| Reactive (ReAct) | Tool loop (built-in) | Open-ended tasks where the agent decides what to do next |
| Parallel (Fan-out/Fan-in) | Background agents | Independent sub-tasks that can run concurrently |
| Deliberate (Planning) | Workflow definitions | Complex multi-step tasks with predefined procedures |
| Reflective (Self-Critique) | Critic agent or self-review step | High-stakes outputs needing quality verification |
| Hierarchical (Delegation) | Handoff to worker agents | Complex problems with clear domain boundaries |
| Collaborative (Multi-Agent Debate) | Agent messaging | Decisions benefiting from diverse viewpoints |

---

## 1. Sequential

The agent follows a fixed sequence of steps defined in its persona instructions. Each step runs to completion before the next begins. The LLM does not decide the order -- the instructions dictate it.

**When to use:** Predictable multi-step workflows where the order is known upfront. Report generation, data extraction pipelines, onboarding checklists.

**How to implement:** Define the steps in the persona seed's instructions. The agent's tool loop follows them in order.

```csharp
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "daily-report-generator",
    description: "Generates daily market reports following a fixed procedure.",
    instructions: """
    You generate a daily market report. Follow these steps in order:

    1. Use `stock_quote` to get the current price for AAPL, MSFT, and GOOGL
    2. Use `stock_history` to get the 5-day price history for each
    3. Use `edgar_company_facts` to get the latest quarterly revenue for each
    4. Write a summary report with price trends and revenue comparison
    5. Save the report using `add_context` with key "daily-report-{date}"

    Do not skip steps. Do not reorder steps.
    """));
```

The LLM sees these instructions in its system prompt and executes the tool calls in the specified order.

---

## 2. Reactive (ReAct)

The default behavior pattern. The LLM reasons about the current situation, decides which tool to call, observes the result, and repeats until the goal is met. This is the standard reason-act-observe loop that every agent runs by default.

**When to use:** Most tasks. Any situation where the agent needs to decide at runtime what to do next based on the current state.

**How to implement:** No special configuration. Every agent uses this pattern by default. The `FunctionInvokingChatClient` handles the loop automatically:

```csharp
// The reactive loop is built into the agent runtime
AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions
{
    Name = "assistant",
    ChatOptions = new ChatOptions
    {
        Instructions = "You are a helpful assistant.",
        Tools = tools,
    },
});
```

The LLM receives all available tools and decides which to call based on the user's request. If the first tool call does not produce a satisfactory result, the LLM reasons about what to try next.

---

## 3. Parallel (Fan-out/Fan-in)

The main agent spawns multiple background tasks on specialist agents, waits for them to complete, and synthesizes the results. Tasks run concurrently.

**When to use:** Independent sub-tasks that do not depend on each other. Researching multiple topics simultaneously. Gathering data from multiple sources in parallel.

**How to implement:** Use background agent tools. Any persona in the `AgentRegistry` is automatically available as a background agent.

```
LLM decides to fan out:
  -> background_agents_start_task("financial-analyst", "Analyze AAPL financial health")
  -> background_agents_start_task("financial-analyst", "Analyze MSFT financial health")
  -> background_agents_start_task("financial-analyst", "Analyze GOOGL financial health")
  -> background_agents_wait_for_first_completion()
  -> background_agents_get_all_tasks()  // check which are done
  -> background_agents_get_task_results(task_id)  // collect each result
  -> Synthesize comparative report from all three results
```

You can also use the workflow executor for structured parallel execution:

```csharp
// Programmatic concurrent execution via MAF workflow builder
var agents = agentNames
    .Select(name => registry.Resolve(name))
    .Where(a => a is not null)
    .ToList();

var workflow = AgentWorkflowBuilder.BuildConcurrent(agents);
var input = new ChatMessage(ChatRole.User, userInput);
await using var run = await InProcessExecution.RunAsync(workflow, input, cancellationToken: ct);
```

---

## 4. Deliberate (Planning)

Workflow definitions decompose goals into named steps. Instead of relying on the LLM to figure out the steps at runtime, you define the procedure upfront. The agent follows the workflow, using its tools to execute each step.

**When to use:** Complex multi-step tasks with well-known procedures. Compliance reviews, deployment checklists, data migration pipelines.

**How to implement:** Create workflows using the workflow tools or seed them at startup. Three execution modes are available:

**Simple workflow** -- a markdown-defined procedure the agent follows:

```csharp
services.AddSingleton<IWorkflowSeed>(new WorkflowSeed(
    agentName: "compliance-reviewer",
    name: "quarterly-review",
    content: """
    # Quarterly Compliance Review

    ## Steps
    1. Pull all transactions from the last quarter
    2. Flag any transaction over $10,000 without documentation
    3. Check each flagged transaction against the exceptions list
    4. Generate a findings report with severity ratings
    5. Create follow-up tasks for any critical findings
    """));
```

The agent calls `run_workflow("quarterly-review", "Run the Q3 2025 review")` and the executor feeds the workflow steps into the LLM as structured instructions.

**Sequential workflow** -- chains multiple agents in order, each receiving the previous agent's output:

```csharp
var workflow = AgentWorkflowBuilder.BuildSequential(agents);
await using var run = await InProcessExecution.RunAsync(workflow, inputMessage, cancellationToken: ct);
```

**Declarative workflow** -- YAML-defined workflow graphs loaded and executed via MAF's `DeclarativeWorkflowBuilder`:

```csharp
var workflow = DeclarativeWorkflowBuilder.Build<ChatMessage>(filePath, options);
await using var run = await InProcessExecution.RunAsync(workflow, inputMessage, cancellationToken: ct);
```

---

## 5. Reflective (Self-Critique)

The agent reviews its own output before delivering it, or hands off to a dedicated critic agent that evaluates quality. The review step catches errors, improves clarity, and verifies that the output meets requirements.

**When to use:** High-stakes outputs: legal documents, financial reports, customer-facing content. Any situation where getting it wrong is expensive.

**How to implement:** Two approaches.

**Self-review via persona instructions:**

```csharp
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "report-writer",
    description: "Writes reports with built-in quality review.",
    instructions: """
    After generating any report:
    1. Review each claim for accuracy against the source data
    2. Check that all requested sections are present
    3. Verify that numbers are consistent across sections
    4. If you find errors, fix them before delivering the final version
    5. State what you checked at the end of the report
    """));
```

**Critic agent via handoff:**

```csharp
// Register a critic persona
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "quality-reviewer",
    description: "Reviews agent outputs for accuracy and completeness.",
    instructions: """
    You are a quality reviewer. When given a document:
    1. Check all factual claims against available data
    2. Identify any logical inconsistencies
    3. Flag missing information
    4. Return a verdict: PASS, REVISE (with specific feedback), or FAIL (with reasons)
    """));

// The main agent's instructions tell it to use the critic
// "After generating the report, use handoff_to_agent('quality-reviewer', report) to get a quality review."
```

---

## 6. Hierarchical (Delegation)

A manager agent decomposes a complex problem into sub-tasks and delegates each to a specialist worker agent via handoff. Workers return results. The manager synthesizes the final output.

**When to use:** Complex problems with clear domain boundaries. Each sub-domain has its own expertise, tools, and knowledge. The manager orchestrates but does not do the domain-specific work.

**How to implement:** Register specialist agents with scoped tools. The manager agent's persona instructs it to delegate:

```csharp
// Manager agent with broad tool access
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "project-manager",
    description: "Orchestrates complex projects by delegating to specialists.",
    instructions: """
    You are a project manager. When given a complex request:
    1. Break it into sub-tasks based on domain expertise
    2. Delegate each sub-task to the appropriate specialist using handoff_to_agent
    3. Review each specialist's response for quality and completeness
    4. Synthesize a final response that integrates all results
    
    Available specialists:
    - financial-analyst: market data, stock analysis, financial reports
    - compliance-reviewer: regulatory checks, policy validation
    - report-writer: document formatting, narrative writing
    """));

// Each specialist has its own scoped tools
services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "financial-analyst",
    toolNames: ["stock_quote", "stock_history", "edgar_company_facts"]));

services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "compliance-reviewer",
    toolNames: ["list_guardrails", "read_guardrail", "read_entity"]));
```

The manager calls `handoff_to_agent` for each sub-task and receives structured results. It then combines them into a coherent final response.

---

## 7. Collaborative (Multi-Agent Debate)

Multiple agents share perspectives on the same problem via messaging. Different personas reason about the same question from different angles. A synthesizer agent collects the perspectives and produces a balanced conclusion.

**When to use:** Decisions that benefit from diverse viewpoints. Risk assessments, strategy decisions, ethical reviews. Cases where a single perspective might miss important considerations.

**How to implement:** Use `IAgentMessaging` for asynchronous inter-agent communication:

```csharp
// Define agents with different perspectives
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "optimist-analyst",
    description: "Analyzes opportunities and upside potential.",
    instructions: "Analyze the given scenario focusing on opportunities, growth potential, and best-case outcomes. Be specific about what could go right."));

services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "risk-analyst",
    description: "Analyzes risks and downside potential.",
    instructions: "Analyze the given scenario focusing on risks, threats, and worst-case outcomes. Be specific about what could go wrong."));
```

The orchestrator fans out the question to both analysts (via background delegation or handoff), collects their perspectives, and synthesizes a balanced view:

```
Orchestrator:
  -> background_agents_start_task("optimist-analyst", "Evaluate expanding into the European market")
  -> background_agents_start_task("risk-analyst", "Evaluate expanding into the European market")
  -> Wait for both to complete
  -> Synthesize: "The optimist sees X, Y, Z. The risk analyst flags A, B, C. Balanced recommendation: ..."
```

For more structured collaboration, agents can exchange messages directly:

```csharp
public interface IAgentMessaging
{
    Task SendAsync(AgentMessage message, CancellationToken cancellationToken = default);
    Task<AgentMessage?> ReceiveAsync(string agentName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentMessage>> ReceiveAllAsync(string agentName, CancellationToken cancellationToken = default);
}
```

---

## Tool Scoping

`IAgentToolsSeed` restricts which tools each agent can see. When the `AgentRegistry` builds an agent from a persona, it filters the global tool set to include only the tools named in that agent's seed.

```csharp
// In AgentRegistry.BuildAgentForPersona
var allTools = _toolsFactory().ToList();
var tools = _toolSeeds.TryGetValue(persona.Name, out var seed)
    ? allTools.Where(t => seed.ToolNames.Contains(t.Name)).ToList()
    : allTools;
```

If no tool seed is registered for an agent, it receives all tools.

Benefits of tool scoping:

- **Focus** -- fewer irrelevant tools means the LLM spends less time reasoning about tools it should not use. A financial analyst does not need to see scheduling tools.
- **Safety** -- limit access to dangerous or sensitive tools. A read-only analyst should not have write tools.
- **Cost** -- fewer tools in the prompt means fewer tokens in tool descriptions, which reduces LLM costs on every turn.

Register a tool seed alongside the persona seed:

```csharp
services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "financial-analyst",
    toolNames: ["stock_quote", "stock_history", "edgar_company_facts"]));
```

---

## Choosing a Pattern

Start with Reactive (the default) and add structure only when the task demands it.

```
Is it a single-step task?
  └─ Yes -> Reactive (default tool loop)
  └─ No
      ├─ Is the sequence of steps known upfront?
      │   └─ Yes -> Sequential (persona instructions) or Deliberate (workflow)
      │   └─ No -> Reactive
      │
      ├─ Are there independent sub-tasks?
      │   └─ Yes -> Parallel (background agents)
      │
      ├─ Does it need domain-specific specialists?
      │   └─ Yes -> Hierarchical (manager delegates to workers)
      │
      ├─ Does the output need quality verification?
      │   └─ Yes -> Reflective (self-review or critic agent)
      │
      └─ Would multiple perspectives improve the decision?
          └─ Yes -> Collaborative (multi-agent debate)
```

Patterns compose. A hierarchical system can use parallel fan-out to run workers concurrently. A deliberate workflow can include a reflective step at the end. A collaborative debate can feed into a sequential report-generation pipeline. Start simple and layer patterns as complexity grows.
