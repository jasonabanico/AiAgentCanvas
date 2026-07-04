# Developer Guide

This guide covers the architecture, data model, extensibility, and operational details of AI Agent Canvas -- a .NET multi-agent copilot platform built on Microsoft's Agent Framework and Azure AI Foundry.

| Page | Description |
|------|-------------|
| [Architecture](developer-guide-architecture.md) | Request flow, project structure, core platform services, dependency patterns, and extension methods |
| [Agent Data](developer-guide-agent-data.md) | Seven markdown-persisted data domains, the Store/ToolProvider/ContextProvider pattern, and creating new domains |
| [Skills & MCP](developer-guide-skills-mcp.md) | Skill persistence, MCP server connections, local skill registry, and adding custom MCP tool-provider projects |
| [RAG Pipeline](developer-guide-rag.md) | Recursive chunking, hybrid search, metadata filtering, LLM reranking, citation, and document ingestion |
| [Security](developer-guide-security.md) | Governance kernel, policy-based tool governance, MCP gateway, rate limiting, and security headers |
| [Adding Agents](developer-guide-adding-agents.md) | Step-by-step guide for creating a custom agent project with component seeding and testing |
| [Behavior Patterns](developer-guide-behavior-patterns.md) | Sequential, reactive, parallel, deliberate, reflective, hierarchical, and collaborative agent patterns |

---

> **[Download the complete PDF guide](guides/AI-Agent-Canvas-Guide.pdf)** | **[AI-First Company Guide](guides/AI-First-Company-Guide.pdf)**
