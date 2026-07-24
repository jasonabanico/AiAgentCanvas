# 1. What Is AI Agent Canvas

AI Agent Canvas is an agent development platform built on .NET 9 and Microsoft Agent Framework. It provides the runtime, orchestration layer, and pre-built capabilities to build AI agents -- from a single standalone agent to a coordinated multi-agent ecosystem.

The platform sits between your business logic and the LLM. You write the pieces that make your agent unique (persona, tools, domain knowledge), and the platform handles the rest: the agent execution loop, context injection, tool governance, streaming, state management, and multi-agent coordination.

## Scales of Deployment

| Scale | Description |
|---|---|
| **Standalone agent** | One agent, one process, one domain focus. The simplest deployment: a single agent with its own persona, tools, and context, running in one host. Good for task-specific use cases like a compliance checker or document analyst. |
| **Multi-agent (in-process)** | Multiple specialists in one process, coordinated through handoff and background delegation. Agents share a host and can transfer conversations between each other or spin off parallel work. The user talks to one agent at a time, but the system routes to whichever specialist fits. |
| **Agent ecosystem** | Multiple hosts connected via A2A protocol over HTTP, with independent deployment and security boundaries. Each host runs its own agents and exposes them as services. Agents discover each other through AgentCards and communicate across network boundaries, owned and deployed by different teams. |

The same codebase supports all three. You start with one agent and add more as the problem demands it. A standalone agent can become part of a multi-agent system by registering it in the agent registry and defining handoff rules. That multi-agent system can later federate with other hosts over A2A without changing the agent code.

## What the Platform Provides

The platform handles the infrastructure so you can focus on domain logic:

- **Agent execution loop** -- the reason-act-observe cycle that drives every agent interaction
- **Context injection** -- automatic assembly of persona, entity memory, user profile, guardrails, and domain knowledge into the system prompt
- **Tool governance** -- policy-based filtering and approval rules applied before any tool executes
- **Streaming** -- real-time Server-Sent Events via the AG-UI protocol, delivering text, tool calls, and state updates to the frontend as they happen
- **State management** -- persistent chat history, entity memory, and scheduled task storage backed by SQLite
- **Multi-agent coordination** -- handoff, background delegation, async messaging, and workflow orchestration across agents

## What You Provide

Your job is to define what makes each agent distinct:

- **Persona** -- the agent's identity, tone, expertise, and behavioral instructions
- **Tools** -- the functions the agent can call to take action in your domain
- **Domain knowledge** -- RAG sources, entity definitions, and contextual data the agent needs to reason well
- **Guardrails** -- boundaries on what the agent should and should not do
- **Skills and workflows** -- multi-step procedures the agent follows to complete complex tasks
