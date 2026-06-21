# AI Agent Canvas

Multi-agent enterprise copilot: .NET 9 backend + Next.js/CopilotKit frontend.

## Architecture

All projects sit flat under `src/`, grouped by solution folders in Visual Studio:

- **AiAgentCanvas** (solution folder) — Framework engine + satellite projects:
  - `AiAgentCanvas.Abstractions` — Shared interfaces and models
  - `AiAgentCanvas.Core` — Agent orchestration, AG-UI endpoint, Azure AI Foundry
  - `AiAgentCanvas.AgentData` — MD-persisted agent data (personas, workflows, etc.)
  - `AiAgentCanvas.Scheduler` — Hangfire job scheduling
  - `AiAgentCanvas.Skills` — Skill store, MCP connections, skill authoring
  - `AiAgentCanvas.Notifications` — Channel-backed notification sink + SSE
  - `AiAgentCanvas.SystemTools` — Optional file I/O and script tools
  - `AiAgentCanvas.Security` — Microsoft Agent Governance Toolkit integration
  - `AiAgentCanvas.Web` — Thin composition root. Wires everything together.
- **Custom** (solution folder + `src/Custom/`) — Business/use-case-specific code:
  - `MCP.MarketData` — SEC EDGAR + Yahoo Finance market data tools
  - `VectorStore.Sqlite` — SQLite vector store for RAG
  - `HelloWorldAgent` — Sample agent (EarningsSurpriseScanner)

## Build & Run

```bash
# Backend
dotnet build AiAgentCanvas.sln
cd src/AiAgentCanvas.Web && dotnet run

# Frontend
cd frontend && npm install && npm run dev
```

## Key conventions

- Agents implement `IAgentService` (in `AiAgentCanvas.Abstractions`) and are registered in `Program.cs`
- Each agent owns its routing via `CanHandle(string userMessageLower)`
- `AgentOrchestrator` (in Core) iterates agents and delegates to the first that returns true
- AG-UI protocol (SSE) endpoint at `/api/copilotkit` lives in `AiAgentCanvas.Core`
- MCP connections implement `IMcpClient` (in Abstractions); agents call skills by name only
- Agents never import HTTP or SDK packages for data — they go through `IMcpClient`
- Azure AI Foundry config lives in `appsettings.json` under `AIFoundry` section
- Host calls `builder.Services.AddAiAgentCanvas(config)` + `app.UseAiAgentCanvas()` from Core
