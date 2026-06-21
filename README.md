# AI Agent Canvas

A multi-agent enterprise copilot built with .NET 9, Azure AI Foundry, CopilotKit, and the AG-UI protocol.

## Architecture

```
Frontend (Next.js + CopilotKit)
        │ AG-UI Protocol (SSE)
        ▼
ASP.NET Core Backend
├── AiAgentCanvas.Core ─── engine: orchestrator, AG-UI endpoint, DI
├── MyFirstAgent ─────── earnings surprise scanner (custom agent)
└── MCP Skills ───────── SEC EDGAR + Alpha Vantage (real APIs)
        │
        ▼
Azure AI Foundry (ChatCompletionsClient)
```

## Project Structure

```
src/
├── AiAgentCanvas/                             # Core engine (framework)
│   ├── AiAgentCanvas.Abstractions/            # IAgentService, IMcpClient, models
│   └── AiAgentCanvas.Core/                    # AG-UI endpoint, orchestrator, DI extensions
├── MCP/                                     # Data connections
│   └── MCP.MarketData/                      # SEC EDGAR + Alpha Vantage
├── MyAgents/                                # Custom agent logic (no HTTP, no SDKs)
│   └── MyFirstAgent/                        # Earnings Surprise Scanner
└── AiAgentCanvas.Web/                         # Thin composition root
frontend/                                    # Next.js + CopilotKit chat UI
```

Three concerns, three folders:
- **`AiAgentCanvas/`** — the framework. Keep it.
- **`MCP/`** — data connections. Add `MCP.Weather/`, `MCP.Calendar/`, etc. as needed.
- **`MyAgents/`** — your agent logic. Agents call MCP skills by name; they never touch HTTP or APIs directly.

## Quick Start

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
- An Azure AI Foundry deployment (or any OpenAI-compatible endpoint)
- A free [Alpha Vantage API key](https://www.alphavantage.co/support/#api-key) (optional, `demo` key works for limited tickers)

### 1. Configure

Edit `src/AiAgentCanvas.Web/appsettings.Development.json`:

```json
{
  "AlphaVantage": {
    "ApiKey": "your-alpha-vantage-key"
  },
  "AIFoundry": {
    "Endpoint": "https://your-resource.openai.azure.com",
    "Key": "your-api-key",
    "DeploymentName": "gpt-4o",
    "UseAzureCredential": false
  }
}
```

### 2. Run the Backend

```bash
cd src/AiAgentCanvas.Web
dotnet run
```

### 3. Run the Frontend

```bash
cd frontend
npm install
npm run dev
```

Open `http://localhost:3000`. Try: *"Scan for earnings surprises"* or *"Scan $NVDA $TSLA $AAPL"*.

### Docker Compose

```bash
AIFOUNDRY_KEY=your-key docker compose up --build
```

## Adding a New Agent

1. Create a class library under `src/MyAgents/`
2. Reference `AiAgentCanvas.Abstractions` and `AiAgentCanvas.Skills`
3. Implement `IAgentService`:

```csharp
public class MyAgent : IAgentService
{
    public string Name => "MyAgent";
    public string Description => "Does something useful";

    public bool CanHandle(string userMessageLower) =>
        userMessageLower.Contains("my-keyword");

    public async Task<AgentResponse> HandleAsync(AgentRequest request, CancellationToken ct)
    {
        // Your logic here
    }

    public async IAsyncEnumerable<string> StreamAsync(AgentRequest request, CancellationToken ct)
    {
        // Your streaming logic here
    }
}
```

4. Register in `Program.cs`:

```csharp
builder.Services.AddSingleton<IAgentService, MyAgent>();
```

No orchestrator changes needed -- each agent owns its routing via `CanHandle`.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Next.js 15, React 19, CopilotKit |
| Protocol | AG-UI (Server-Sent Events) |
| Backend | ASP.NET Core 9, Minimal APIs |
| AI | Azure AI Foundry (`Azure.AI.Inference`) |
| Data | SEC EDGAR (free), Alpha Vantage (free tier) |
| Auth | Azure Identity (DefaultAzureCredential) |
| Skills | MCP-compatible interface |

## License

MIT
