> [AI Agents](ai-agents.md) > AI Agents: Fundamentals

# AI Agents: Fundamentals

AI agents represent a fundamental shift from traditional chatbots and simple LLM wrappers. Where a chatbot follows scripted flows and an LLM wrapper sends a prompt and returns a response, an AI agent **autonomously reasons, plans, and takes action** to accomplish goals.

## Chatbots vs LLM Wrappers vs AI Agents

| Capability | Traditional Chatbot | LLM Wrapper | AI Agent |
|---|---|---|---|
| Natural language understanding | Rule-based / intent matching | Full LLM capability | Full LLM capability |
| Decision making | Scripted decision trees | Single inference call | Multi-step reasoning |
| Tool use | None or hardcoded | None | Dynamic tool selection |
| Memory | Session state only | Stateless | Persistent context |
| Autonomy | None | None | Goal-directed behavior |

A **traditional chatbot** matches user input against predefined intents and follows scripted flows. It cannot handle novel requests.

An **LLM wrapper** sends user input to a language model and returns the response. It gains natural language understanding but remains a single request-response cycle with no ability to take action.

An **AI agent** uses an LLM as its reasoning engine while adding the ability to call tools, maintain context across interactions, and execute multi-step plans autonomously.

## Key Characteristics of AI Agents

### Autonomy

Agents decide **what to do next** without explicit human instruction for each step. Given a goal like "find the latest sales data and summarize trends," an agent determines which tools to call, in what order, and how to synthesize the results.

### Tool Use

Agents interact with external systems through **tools** (also called functions). A tool might query a database, call an API, search documents, or perform calculations. The LLM decides which tool to invoke based on the user's request and the tool's description.

### Reasoning

Before acting, agents reason about the problem. Modern LLMs can break down complex requests into sub-tasks, evaluate which approach is most appropriate, and adjust their plan based on intermediate results.

### Memory and Context

Agents maintain context across interactions. This includes conversation history, retrieved documents (RAG), user preferences, and domain-specific knowledge injected through context providers.

## The Agent Loop

Every AI agent follows a fundamental execution cycle:

```
                +------------------+
                |    PERCEIVE      |
                |  Receive input,  |
                |  gather context  |
                +--------+---------+
                         |
                         v
                +------------------+
                |     REASON       |
                |  Analyze input,  |
                |  plan next step  |
                +--------+---------+
                         |
                         v
                +------------------+
                |      ACT         |
                | Call a tool or   |
                | generate response|
                +--------+---------+
                         |
                         v
                +------------------+
                |    OBSERVE       |
                | Process result,  |
                | decide if done   |
                +--------+---------+
                         |
                    +----+----+
                    |         |
                    v         v
                Continue    Done
                (loop)    (respond)
```

1. **Perceive** -- The agent receives user input along with injected context (system prompts, RAG results, entity data, user profile information).
2. **Reason** -- The LLM analyzes the input and available tools, then decides on the next action. This may involve breaking a complex request into steps.
3. **Act** -- The agent either calls a tool (e.g., querying a database via MCP) or generates a text response. Tool calls are executed by the framework, not the LLM itself.
4. **Observe** -- The tool result is fed back to the LLM. If the task is complete, the agent produces a final response. If not, it loops back to the Reason step.

This loop continues until the agent determines the task is complete or reaches a configured limit.

## Where AI Agent Canvas Fits

AI Agent Canvas is a **multi-agent enterprise copilot platform** built on three pillars:

- **Microsoft Agent Framework (MAF)** for agent orchestration and the LLM execution loop
- **Azure AI Foundry** as the LLM provider (GPT-4o, GPT-4.1, and other models)
- **CopilotKit** with the AG-UI protocol for real-time streaming to the frontend

The platform is designed around separation of concerns:

```
+---------------------------------------------+
|             AiAgentCanvas.Web               |
|          (Composition Root)                  |
+-----+------------------+--------------------+
      |                  |                    |
      v                  v                    v
+----------+     +-----------+     +------------------+
|   Core   |     |    MCP    |     |   MyAgents       |
| (Engine) |     |  (Data)   |     | (Custom Logic)   |
+----------+     +-----------+     +------------------+
```

- **Core** handles the agent execution loop, tool registry, context providers, and AG-UI streaming. It never changes per use case.
- **MCP** provides data connections. Each MCP project implements data access for a specific domain.
- **MyAgents** contains custom agent reasoning logic. Agents are pure reasoning -- no HTTP calls, no SDK imports for data. They access data exclusively through tools provided by MCP connections.

This architecture means you can build enterprise copilots by composing **agents** (reasoning), **MCP connections** (data), and **context providers** (behavioral instructions) without modifying the core platform.

---

