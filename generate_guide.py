from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor
from reportlab.lib.enums import TA_CENTER, TA_LEFT, TA_JUSTIFY
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle,
    KeepTogether, HRFlowable, Preformatted
)
from reportlab.lib import colors

OUTPUT = "AgentOpsHub_Guide.pdf"

# ── Colours ──────────────────────────────────────────────────────────
BRAND      = HexColor("#1a1a2e")
ACCENT     = HexColor("#0f3460")
HIGHLIGHT  = HexColor("#e94560")
LIGHT_BG   = HexColor("#f5f5f5")
CODE_BG    = HexColor("#f0f0f0")
DARK_TEXT   = HexColor("#222222")
MID_TEXT    = HexColor("#555555")
WHITE      = colors.white

# ── Styles ───────────────────────────────────────────────────────────
styles = getSampleStyleSheet()

styles.add(ParagraphStyle(
    name="CoverTitle",
    fontName="Helvetica-Bold",
    fontSize=32,
    textColor=WHITE,
    alignment=TA_CENTER,
    leading=40,
    spaceAfter=12,
))
styles.add(ParagraphStyle(
    name="CoverSubtitle",
    fontName="Helvetica",
    fontSize=14,
    textColor=HexColor("#cccccc"),
    alignment=TA_CENTER,
    leading=20,
    spaceAfter=6,
))
styles.add(ParagraphStyle(
    name="H1",
    fontName="Helvetica-Bold",
    fontSize=22,
    textColor=BRAND,
    spaceBefore=24,
    spaceAfter=10,
    leading=28,
))
styles.add(ParagraphStyle(
    name="H2",
    fontName="Helvetica-Bold",
    fontSize=16,
    textColor=ACCENT,
    spaceBefore=18,
    spaceAfter=8,
    leading=22,
))
styles.add(ParagraphStyle(
    name="H3",
    fontName="Helvetica-Bold",
    fontSize=13,
    textColor=DARK_TEXT,
    spaceBefore=12,
    spaceAfter=6,
    leading=18,
))
styles.add(ParagraphStyle(
    name="Body",
    fontName="Helvetica",
    fontSize=10.5,
    textColor=DARK_TEXT,
    spaceBefore=4,
    spaceAfter=6,
    leading=15,
    alignment=TA_JUSTIFY,
))
styles.add(ParagraphStyle(
    name="BulletItem",
    fontName="Helvetica",
    fontSize=10.5,
    textColor=DARK_TEXT,
    spaceBefore=2,
    spaceAfter=2,
    leading=15,
    leftIndent=20,
    bulletIndent=8,
    bulletFontName="Helvetica",
    bulletFontSize=10.5,
))
styles.add(ParagraphStyle(
    name="CodeBlock",
    fontName="Courier",
    fontSize=8.5,
    textColor=DARK_TEXT,
    spaceBefore=6,
    spaceAfter=6,
    leading=12,
    leftIndent=12,
    rightIndent=12,
    backColor=CODE_BG,
    borderPadding=(8, 8, 8, 8),
))
styles.add(ParagraphStyle(
    name="Caption",
    fontName="Helvetica-Oblique",
    fontSize=9,
    textColor=MID_TEXT,
    alignment=TA_CENTER,
    spaceBefore=4,
    spaceAfter=12,
))
styles.add(ParagraphStyle(
    name="TOCEntry",
    fontName="Helvetica",
    fontSize=12,
    textColor=ACCENT,
    spaceBefore=6,
    spaceAfter=6,
    leading=16,
    leftIndent=20,
))
styles.add(ParagraphStyle(
    name="Footer",
    fontName="Helvetica",
    fontSize=8,
    textColor=MID_TEXT,
    alignment=TA_CENTER,
))

# ── Helpers ──────────────────────────────────────────────────────────
def h1(text):   return Paragraph(text, styles["H1"])
def h2(text):   return Paragraph(text, styles["H2"])
def h3(text):   return Paragraph(text, styles["H3"])
def body(text): return Paragraph(text, styles["Body"])
def bullet(text): return Paragraph(f"•  {text}", styles["BulletItem"])
def code(text): return Preformatted(text, styles["CodeBlock"])
def caption(text): return Paragraph(text, styles["Caption"])
def spacer(h=0.15): return Spacer(1, h * inch)
def hr(): return HRFlowable(width="100%", thickness=1, color=HexColor("#dddddd"), spaceAfter=12, spaceBefore=12)

def table_block(data, col_widths=None):
    t = Table(data, colWidths=col_widths, hAlign="LEFT")
    t.setStyle(TableStyle([
        ("BACKGROUND",   (0, 0), (-1, 0), ACCENT),
        ("TEXTCOLOR",    (0, 0), (-1, 0), WHITE),
        ("FONTNAME",     (0, 0), (-1, 0), "Helvetica-Bold"),
        ("FONTSIZE",     (0, 0), (-1, 0), 10),
        ("FONTNAME",     (0, 1), (-1, -1), "Helvetica"),
        ("FONTSIZE",     (0, 1), (-1, -1), 9.5),
        ("BACKGROUND",   (0, 1), (-1, -1), LIGHT_BG),
        ("GRID",         (0, 0), (-1, -1), 0.5, HexColor("#cccccc")),
        ("VALIGN",       (0, 0), (-1, -1), "TOP"),
        ("TOPPADDING",   (0, 0), (-1, -1), 6),
        ("BOTTOMPADDING",(0, 0), (-1, -1), 6),
        ("LEFTPADDING",  (0, 0), (-1, -1), 8),
        ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [LIGHT_BG, WHITE]),
    ]))
    return t

# ── Cover page (drawn via onFirstPage) ──────────────────────────────
def draw_cover(canvas_obj, doc):
    w, h = letter
    canvas_obj.saveState()
    canvas_obj.setFillColor(BRAND)
    canvas_obj.rect(0, 0, w, h, fill=1, stroke=0)
    canvas_obj.setFillColor(HIGHLIGHT)
    canvas_obj.rect(0, h * 0.42, w, 4, fill=1, stroke=0)
    canvas_obj.restoreState()

def draw_later_pages(canvas_obj, doc):
    w, h = letter
    canvas_obj.saveState()
    canvas_obj.setFillColor(HexColor("#f8f8f8"))
    canvas_obj.rect(0, h - 30, w, 30, fill=1, stroke=0)
    canvas_obj.setFont("Helvetica", 7.5)
    canvas_obj.setFillColor(MID_TEXT)
    canvas_obj.drawString(inch, h - 20, "AgentOpsHub Guide")
    canvas_obj.drawRightString(w - inch, h - 20, f"Page {doc.page}")
    canvas_obj.setFillColor(ACCENT)
    canvas_obj.rect(0, h - 31, w, 1, fill=1, stroke=0)
    canvas_obj.restoreState()


# ── Build document ───────────────────────────────────────────────────
doc = SimpleDocTemplate(
    OUTPUT,
    pagesize=letter,
    leftMargin=0.9*inch,
    rightMargin=0.9*inch,
    topMargin=0.75*inch,
    bottomMargin=0.75*inch,
    title="AgentOpsHub - Developer Guide",
    author="AgentOpsHub",
    subject="Multi-Agent Enterprise Copilot Framework",
)

story = []

# ━━━━━━━━━━━  COVER PAGE  ━━━━━━━━━━━
story.append(Spacer(1, 2.2*inch))
story.append(Paragraph("AgentOpsHub", styles["CoverTitle"]))
story.append(Spacer(1, 0.15*inch))
story.append(Paragraph("Developer Guide", ParagraphStyle(
    "CoverTag", parent=styles["CoverSubtitle"], fontSize=18, textColor=HIGHLIGHT,
    fontName="Helvetica-Bold")))
story.append(Spacer(1, 0.4*inch))
story.append(Paragraph("Multi-Agent Enterprise Copilot Framework", styles["CoverSubtitle"]))
story.append(Paragraph(".NET 9  |  Azure AI Foundry  |  CopilotKit  |  AG-UI Protocol", styles["CoverSubtitle"]))
story.append(Spacer(1, 1.0*inch))
story.append(Paragraph("v1.0  |  June 2026", ParagraphStyle(
    "CoverDate", parent=styles["CoverSubtitle"], fontSize=10, textColor=HexColor("#999999"))))
story.append(PageBreak())

# ━━━━━━━━━━━  TABLE OF CONTENTS  ━━━━━━━━━━━
story.append(h1("Table of Contents"))
story.append(hr())
toc_items = [
    "1.  Introduction",
    "2.  Architecture Overview",
    "3.  Project Structure",
    "4.  How It Works End-to-End",
    "5.  Core Abstractions",
    "6.  The Agent Tool Loop",
    "7.  MCP: Data Connections",
    "8.  Building Your First Agent",
    "9.  Building an MCP Client",
    "10. Framework Features",
    "11. Configuration Reference",
    "12. Quick Start",
]
for item in toc_items:
    story.append(Paragraph(item, styles["TOCEntry"]))
story.append(PageBreak())

# ━━━━━━━━━━━  1. INTRODUCTION  ━━━━━━━━━━━
story.append(h1("1. Introduction"))
story.append(hr())
story.append(body(
    "AgentOpsHub is a <b>multi-agent enterprise copilot framework</b> built with .NET 9, "
    "Azure AI Foundry, and CopilotKit. It provides the scaffolding to build, deploy, and "
    "operate AI agents that can reason over data, call tools autonomously, and stream "
    "responses to a chat UI via the AG-UI protocol."
))
story.append(spacer())
story.append(body("The framework is designed around three principles:"))
story.append(bullet("<b>Separation of concerns</b> — The core engine, data connections, and agent logic each live in their own folder and never cross boundaries."))
story.append(bullet("<b>Agent autonomy</b> — Agents decide which tools to call and when, using an LLM-driven tool loop rather than hardcoded sequences."))
story.append(bullet("<b>Extensibility</b> — Adding a new agent or data source is a single class plus one line in Program.cs. No orchestrator changes needed."))
story.append(spacer())
story.append(h2("What You Get Out of the Box"))
story.append(bullet("AG-UI SSE endpoint for CopilotKit integration"))
story.append(bullet("LLM-based routing with keyword fast-path"))
story.append(bullet("Agentic tool loop (LLM decides tool calls iteratively)"))
story.append(bullet("Aggregate MCP client supporting multiple data sources"))
story.append(bullet("Conversation memory (full history passed to agents)"))
story.append(bullet("Structured logging and observability"))
story.append(bullet("HTTP resilience with automatic retries"))
story.append(PageBreak())

# ━━━━━━━━━━━  2. ARCHITECTURE  ━━━━━━━━━━━
story.append(h1("2. Architecture Overview"))
story.append(hr())
story.append(body(
    "The system has four layers. The frontend sends user messages over SSE. The backend "
    "routes them to the right agent, which uses an LLM to reason and call tools. Tools "
    "are provided by MCP clients that connect to external APIs."
))
story.append(spacer())
story.append(code(
    "Frontend (Next.js + CopilotKit)\n"
    "        | AG-UI Protocol (SSE)\n"
    "        v\n"
    "ASP.NET Core Backend\n"
    " +-- AgUiEndpoint -----> receives POST, streams SSE events\n"
    " +-- AgentOrchestrator -> routes to the right agent\n"
    " +-- ToolEnabledAgent --> LLM tool loop (reason + act)\n"
    " +-- AggregateMcpClient -> dispatches tool calls to MCP clients\n"
    "        |\n"
    "        v\n"
    "Azure AI Foundry (ChatCompletionsClient)"
))
story.append(caption("Figure 1: End-to-end request flow"))
story.append(spacer())
story.append(h2("Key Components"))
story.append(spacer())
story.append(table_block([
    ["Component", "Location", "Responsibility"],
    ["AgUiEndpoint", "Core/Endpoints/", "HTTP handler; SSE streaming; conversation memory"],
    ["AgentOrchestrator", "Core/Services/", "Routes messages to agents (keyword + LLM fallback)"],
    ["ToolEnabledAgentBase", "Core/Agents/", "Abstract base class implementing the agentic tool loop"],
    ["AggregateMcpClient", "Core/Services/", "Composites multiple IMcpClient instances by skill name"],
    ["IAgentService", "Abstractions/", "Interface every agent implements"],
    ["IMcpClient", "Abstractions/", "Interface every data connection implements"],
], col_widths=[1.6*inch, 1.4*inch, 3.0*inch]))
story.append(PageBreak())

# ━━━━━━━━━━━  3. PROJECT STRUCTURE  ━━━━━━━━━━━
story.append(h1("3. Project Structure"))
story.append(hr())
story.append(body("Three concerns, three folders. Each has a clear responsibility and dependency direction:"))
story.append(spacer())
story.append(code(
    "src/\n"
    "+-- AgentOpsHub/                        # Core engine (FRAMEWORK)\n"
    "|   +-- AgentOpsHub.Abstractions/       # Interfaces, models (zero deps)\n"
    "|   +-- AgentOpsHub.Core/               # Endpoint, orchestrator, tool loop\n"
    "|\n"
    "+-- MCP/                                # Data connections\n"
    "|   +-- MCP.MarketData/                 # SEC EDGAR + Alpha Vantage\n"
    "|   +-- MCP.Weather/                    # (add your own)\n"
    "|\n"
    "+-- MyAgents/                           # Custom agent logic\n"
    "|   +-- MyFirstAgent/                   # Earnings Surprise Scanner\n"
    "|   +-- MySecondAgent/                  # (add your own)\n"
    "|\n"
    "+-- AgentOpsHub.Web/                    # Thin composition root\n"
    "\n"
    "frontend/                               # Next.js + CopilotKit chat UI"
))
story.append(spacer())
story.append(h2("Dependency Direction"))
story.append(body(
    "Dependencies always flow <b>inward</b> toward Abstractions. "
    "Agents and MCP clients reference the framework; the framework never references them."
))
story.append(spacer())
story.append(code(
    "AgentOpsHub.Web ---> Core ---> Abstractions <--- MCP.MarketData\n"
    "                       ^                    <--- MyFirstAgent\n"
    "                       |\n"
    "                  MyFirstAgent (inherits ToolEnabledAgentBase from Core)"
))
story.append(spacer())
story.append(body(
    "<b>Abstractions</b> has zero NuGet dependencies — it is entirely SDK-agnostic. "
    "This means you can swap Azure AI Foundry for OpenAI, Ollama, or any other provider "
    "by changing Core only. Agent logic and MCP clients never touch LLM SDKs directly."
))
story.append(PageBreak())

# ━━━━━━━━━━━  4. END-TO-END FLOW  ━━━━━━━━━━━
story.append(h1("4. How It Works End-to-End"))
story.append(hr())
story.append(body("Here is what happens when a user types a message in the CopilotKit chat UI:"))
story.append(spacer())

steps = [
    ("<b>1. Frontend sends POST</b> — CopilotKit sends a POST request to <font face='Courier' size='9'>/api/copilotkit</font> "
     "with the full message history as a JSON array."),
    ("<b>2. AgUiEndpoint receives it</b> — Extracts the last user message, builds conversation history "
     "into an AgentRequest, and sets up SSE streaming headers."),
    ("<b>3. AgentOrchestrator routes</b> — Tries each agent's <font face='Courier' size='9'>CanHandle()</font> method (fast keyword match). "
     "If none match, falls back to LLM intent classification using agent names and descriptions."),
    ("<b>4. Agent.StreamAsync() runs</b> — The selected agent starts its tool loop. "
     "The base class builds the initial message list from history + system prompt + user message."),
    ("<b>5. LLM decides tool calls</b> — The LLM sees the available tools (from MCP skills) and either "
     "returns tool_calls or a text response."),
    ("<b>6. Tools are executed</b> — For each tool call, the framework calls "
     "<font face='Courier' size='9'>IMcpClient.ExecuteAsync()</font> and adds the result back to the message list."),
    ("<b>7. Loop continues</b> — Steps 5–6 repeat until the LLM produces a final text answer "
     "(or hits MaxToolIterations)."),
    ("<b>8. Response streams</b> — The final text response is streamed via SSE events "
     "(<font face='Courier' size='9'>text.message.content</font>) back to the frontend."),
]
for step in steps:
    story.append(body(step))
    story.append(spacer(0.05))

story.append(PageBreak())

# ━━━━━━━━━━━  5. CORE ABSTRACTIONS  ━━━━━━━━━━━
story.append(h1("5. Core Abstractions"))
story.append(hr())
story.append(body(
    "The Abstractions project defines the contracts that all agents and data connections implement. "
    "It has <b>zero external dependencies</b>, making it the stable foundation of the framework."
))

story.append(h2("IAgentService"))
story.append(body("Every agent implements this interface. It defines identity, routing, and execution:"))
story.append(code(
    "public interface IAgentService\n"
    "{\n"
    "    string Name { get; }\n"
    "    string Description { get; }\n"
    "    bool CanHandle(string userMessageLower);\n"
    "    Task<AgentResponse> HandleAsync(AgentRequest request, CancellationToken ct);\n"
    "    IAsyncEnumerable<string> StreamAsync(AgentRequest request, CancellationToken ct);\n"
    "}"
))
story.append(spacer())

story.append(h2("IMcpClient"))
story.append(body("Every data connection implements this interface. It exposes tools that agents can call:"))
story.append(code(
    "public interface IMcpClient\n"
    "{\n"
    "    Task<string> ExecuteAsync(string toolName, string paramsJson, CancellationToken ct);\n"
    "    IReadOnlyList<SkillDefinition> ListSkills();\n"
    "}\n"
    "\n"
    "public sealed record SkillDefinition(\n"
    "    string Name,\n"
    "    string Description,\n"
    "    IReadOnlyDictionary<string, string> Parameters);"
))
story.append(spacer())

story.append(h2("AgentRequest & AgentResponse"))
story.append(body("SDK-agnostic request/response models that flow through the system:"))
story.append(code(
    "public sealed class AgentRequest\n"
    "{\n"
    "    public required string ThreadId { get; init; }\n"
    "    public required string RunId { get; init; }\n"
    "    public required string UserMessage { get; init; }\n"
    "    public List<ChatMessage> History { get; init; } = [];\n"
    "}\n"
    "\n"
    "public sealed class AgentResponse\n"
    "{\n"
    "    public required string Content { get; init; }\n"
    "    public string? AgentName { get; init; }\n"
    "    public Dictionary<string, object>? Metadata { get; init; }\n"
    "}"
))
story.append(spacer())

story.append(h2("ToolDefinition"))
story.append(body(
    "SDK-agnostic representation of a tool for LLM function-calling. "
    "The <font face='Courier' size='9'>ParametersJsonSchema</font> field stores a raw JSON Schema string "
    "so Abstractions stays decoupled from any LLM SDK. Core converts this to the SDK-specific format."
))
story.append(code(
    "public sealed class ToolDefinition\n"
    "{\n"
    "    public required string Name { get; init; }\n"
    "    public required string Description { get; init; }\n"
    "    public required string ParametersJsonSchema { get; init; }\n"
    "}"
))
story.append(PageBreak())

# ━━━━━━━━━━━  6. THE AGENT TOOL LOOP  ━━━━━━━━━━━
story.append(h1("6. The Agent Tool Loop"))
story.append(hr())
story.append(body(
    "The <b>ToolEnabledAgentBase</b> class in Core is the heart of the framework. "
    "It implements the agentic loop that makes agents truly autonomous — the LLM decides "
    "which tools to call and when, rather than following a hardcoded sequence."
))
story.append(spacer())

story.append(h2("How the Loop Works"))
story.append(code(
    "1. Build messages:  [SystemPrompt] + [History] + [UserMessage]\n"
    "2. Add tools:       Convert MCP skills -> ToolDefinitions -> SDK tool format\n"
    "3. Call LLM:        Send messages + tools to ChatCompletionsClient\n"
    "4. Check response:\n"
    "   - If tool_calls:  Execute each via IMcpClient, add results, GOTO 3\n"
    "   - If text:        Stream the final answer to the user, DONE\n"
    "5. Safety:           MaxToolIterations (default 10) prevents runaway loops"
))
story.append(spacer())

story.append(h2("What Subclasses Override"))
story.append(spacer())
story.append(table_block([
    ["Method", "Required", "Purpose"],
    ["Name", "Yes", "Agent identity string"],
    ["Description", "Yes", "Used by LLM routing to match intent"],
    ["CanHandle()", "Yes", "Fast keyword matching for routing"],
    ["GetSystemPrompt()", "Yes", "Returns the system prompt for this agent's task"],
    ["GetAvailableTools()", "No", "Filter which MCP tools this agent can use (default: all)"],
    ["MaxToolIterations", "No", "Override the safety limit (default: 10)"],
], col_widths=[1.8*inch, 0.8*inch, 3.4*inch]))
story.append(spacer())

story.append(h2("Why This Matters"))
story.append(body(
    "Before this pattern, agents had to manually orchestrate tool calls: "
    "\"call SEC EDGAR, then call stock quote, then call RSI, then format results, then call LLM.\" "
    "That is a <b>prompt wrapper</b>, not an agent."
))
story.append(body(
    "With the tool loop, you just tell the LLM what data to gather and how to analyze it. "
    "The LLM figures out the sequence, handles errors, asks follow-up questions, and "
    "produces a final answer. This is what makes it a true <b>agent</b>."
))
story.append(PageBreak())

# ━━━━━━━━━━━  7. MCP: DATA CONNECTIONS  ━━━━━━━━━━━
story.append(h1("7. MCP: Data Connections"))
story.append(hr())
story.append(body(
    "MCP (Model Context Protocol) clients are the bridge between agents and external data. "
    "Each client exposes a set of named <b>skills</b> that agents can call through the tool loop. "
    "Agents never make HTTP calls directly — they go through IMcpClient."
))
story.append(spacer())

story.append(h2("AggregateMcpClient"))
story.append(body(
    "The framework supports <b>multiple MCP clients</b> registered simultaneously. "
    "The AggregateMcpClient in Core composites them into a single IMcpClient that agents receive. "
    "It dispatches tool calls by skill name — the first client that registered a given skill handles it."
))
story.append(spacer())
story.append(code(
    "// Register multiple MCP clients in Program.cs\n"
    "builder.Services.AddMarketDataMcpClient();  // SEC EDGAR + Alpha Vantage\n"
    "builder.Services.AddWeatherMcpClient();      // OpenWeatherMap\n"
    "builder.Services.AddCalendarMcpClient();     // Google Calendar\n"
    "\n"
    "// All skills from all clients are available to all agents"
))
story.append(spacer())

story.append(h2("Built-in: MCP.MarketData"))
story.append(body("The included MarketDataMcpClient provides three skills for financial data:"))
story.append(spacer())
story.append(table_block([
    ["Skill Name", "Source", "Description"],
    ["edgar_company_facts", "SEC EDGAR", "Fetch financial filings (EPS, revenue, assets) by ticker"],
    ["stock_quote", "Alpha Vantage", "Get current stock price and daily change"],
    ["stock_technicals", "Alpha Vantage", "Get RSI, SMA, EMA, MACD indicators"],
], col_widths=[1.7*inch, 1.3*inch, 3.0*inch]))
story.append(spacer())

story.append(h2("Resilience"))
story.append(body(
    "MCP.MarketData uses <b>Microsoft.Extensions.Http.Resilience</b> for automatic retries "
    "with exponential backoff. Named HttpClients (\"SEC\", \"AlphaVantage\") are configured with "
    "timeouts and the standard resilience handler. Failed calls return structured error JSON "
    "instead of throwing — the LLM can see the error and decide how to proceed."
))
story.append(PageBreak())

# ━━━━━━━━━━━  8. BUILDING YOUR FIRST AGENT  ━━━━━━━━━━━
story.append(h1("8. Building Your First Agent"))
story.append(hr())
story.append(body(
    "Building a new agent takes three steps: create a class library, implement the agent, "
    "and register it in Program.cs. The entire process takes about 5 minutes."
))
story.append(spacer())

story.append(h2("Step 1: Create the Project"))
story.append(code(
    "# Create a new class library under src/MyAgents/\n"
    "dotnet new classlib -n MySecondAgent -o src/MyAgents/MySecondAgent\n"
    "\n"
    "# Add a reference to Core (gives you Abstractions + ToolEnabledAgentBase)\n"
    "cd src/MyAgents/MySecondAgent\n"
    "dotnet add reference ../../AgentOpsHub/AgentOpsHub.Core/AgentOpsHub.Core.csproj\n"
    "\n"
    "# Add project to the solution\n"
    "cd ../../..\n"
    "dotnet sln add src/MyAgents/MySecondAgent/MySecondAgent.csproj"
))
story.append(spacer())

story.append(h2("Step 2: Implement the Agent"))
story.append(code(
    "using AgentOpsHub.Abstractions;\n"
    "using AgentOpsHub.Core.Agents;\n"
    "using Azure.AI.Inference;\n"
    "using Microsoft.Extensions.Logging;\n"
    "\n"
    "namespace MySecondAgent;\n"
    "\n"
    "public sealed class PortfolioAnalysisAgent : ToolEnabledAgentBase\n"
    "{\n"
    "    public override string Name => \"PortfolioAnalysis\";\n"
    "    public override string Description =>\n"
    "        \"Analyzes a stock portfolio for risk, diversification, and performance.\";\n"
    "\n"
    "    public PortfolioAnalysisAgent(\n"
    "        ChatCompletionsClient chatClient,\n"
    "        IMcpClient mcpClient,\n"
    "        ILogger<PortfolioAnalysisAgent> logger)\n"
    "        : base(chatClient, mcpClient, logger) { }\n"
    "\n"
    "    public override bool CanHandle(string userMessageLower) =>\n"
    "        userMessageLower.Contains(\"portfolio\") ||\n"
    "        userMessageLower.Contains(\"diversif\") ||\n"
    "        userMessageLower.Contains(\"risk analysis\");\n"
    "\n"
    "    protected override string GetSystemPrompt(AgentRequest request) => \"\"\"\n"
    "        You are a portfolio analyst. Use the available tools to:\n"
    "        1. Look up fundamentals for each ticker in the user's portfolio\n"
    "        2. Get current prices and technical indicators\n"
    "        3. Assess sector concentration, valuation risk, and momentum\n"
    "        4. Produce a diversification score and actionable recommendations\n"
    "        Output a markdown report with tables and clear reasoning.\n"
    "        \"\"\";\n"
    "}"
))
story.append(spacer())

story.append(h2("Step 3: Register in Program.cs"))
story.append(code(
    "// Add one line to Program.cs:\n"
    "builder.Services.AddSingleton<IAgentService, PortfolioAnalysisAgent>();"
))
story.append(spacer())
story.append(body(
    "That is it. The agent is automatically:"
))
story.append(bullet("Discovered by the AgentOrchestrator for routing"))
story.append(bullet("Given all MCP tools from all registered clients"))
story.append(bullet("Equipped with conversation memory"))
story.append(bullet("Streaming responses via SSE to the frontend"))
story.append(PageBreak())

# ━━━━━━━━━━━  9. BUILDING AN MCP CLIENT  ━━━━━━━━━━━
story.append(h1("9. Building an MCP Client"))
story.append(hr())
story.append(body(
    "MCP clients connect your agents to external data. Each client is a class library that "
    "implements <font face='Courier' size='9'>IMcpClient</font> and defines a set of skills."
))
story.append(spacer())

story.append(h2("Step 1: Create the Project"))
story.append(code(
    "dotnet new classlib -n MCP.Weather -o src/MCP/MCP.Weather\n"
    "cd src/MCP/MCP.Weather\n"
    "dotnet add reference ../../AgentOpsHub/AgentOpsHub.Abstractions\n"
    "dotnet add package Microsoft.Extensions.Http.Resilience"
))
story.append(spacer())

story.append(h2("Step 2: Implement IMcpClient"))
story.append(code(
    "public sealed class WeatherMcpClient : IMcpClient\n"
    "{\n"
    "    private static readonly IReadOnlyList<SkillDefinition> Skills =\n"
    "    [\n"
    "        new(\"get_weather\", \"Get current weather for a city\",\n"
    "            new Dictionary<string, string>\n"
    "            {\n"
    "                [\"city\"] = \"City name (e.g. London, New York)\"\n"
    "            }),\n"
    "    ];\n"
    "\n"
    "    public IReadOnlyList<SkillDefinition> ListSkills() => Skills;\n"
    "\n"
    "    public async Task<string> ExecuteAsync(\n"
    "        string toolName, string paramsJson, CancellationToken ct)\n"
    "    {\n"
    "        return toolName switch\n"
    "        {\n"
    "            \"get_weather\" => await FetchWeather(paramsJson, ct),\n"
    "            _ => JsonSerializer.Serialize(new { error = \"Unknown\" }),\n"
    "        };\n"
    "    }\n"
    "}"
))
story.append(spacer())

story.append(h2("Step 3: Create a Registration Extension"))
story.append(code(
    "public static class WeatherServiceExtensions\n"
    "{\n"
    "    public static IServiceCollection AddWeatherMcpClient(\n"
    "        this IServiceCollection services)\n"
    "    {\n"
    "        services.AddHttpClient(\"Weather\", client =>\n"
    "            client.Timeout = TimeSpan.FromSeconds(10))\n"
    "            .AddStandardResilienceHandler();\n"
    "\n"
    "        services.AddKeyedSingleton<IMcpClient,\n"
    "            WeatherMcpClient>(\"mcp-individual\");\n"
    "        return services;\n"
    "    }\n"
    "}"
))
story.append(spacer())

story.append(h2("Step 4: Register in Program.cs"))
story.append(code(
    "builder.Services.AddWeatherMcpClient();\n"
    "// Skills automatically appear in AggregateMcpClient"
))
story.append(PageBreak())

# ━━━━━━━━━━━  10. FRAMEWORK FEATURES  ━━━━━━━━━━━
story.append(h1("10. Framework Features"))
story.append(hr())

story.append(h2("Conversation Memory"))
story.append(body(
    "The AG-UI endpoint automatically populates <font face='Courier' size='9'>AgentRequest.History</font> "
    "from the full message array sent by CopilotKit. Agents receive the complete conversation "
    "context, enabling multi-turn interactions. The ToolEnabledAgentBase includes this history "
    "in the LLM message list so the model has full context."
))
story.append(spacer())

story.append(h2("LLM-Based Routing"))
story.append(body(
    "The AgentOrchestrator uses a two-tier routing strategy:"
))
story.append(bullet("<b>Fast path:</b> Each agent's <font face='Courier' size='9'>CanHandle()</font> method does keyword matching. If any agent matches, it is selected instantly with zero LLM cost."))
story.append(bullet("<b>Slow path:</b> If no agent matches, the orchestrator sends the user message to the LLM with a list of all agent names and descriptions. The LLM picks the best agent. This handles natural language queries that do not contain exact keywords."))
story.append(spacer())

story.append(h2("Structured Logging"))
story.append(body(
    "All framework components use <font face='Courier' size='9'>ILogger</font> with structured log properties. "
    "Key events logged:"
))
story.append(bullet("AG-UI request received (ThreadId, message count)"))
story.append(bullet("Agent routing decisions (which agent, method used)"))
story.append(bullet("Tool execution timing (tool name, elapsed ms)"))
story.append(bullet("Streaming completion timing"))
story.append(bullet("Errors and fallbacks with context"))
story.append(spacer())

story.append(h2("Error Resilience"))
story.append(body(
    "MCP clients use <font face='Courier' size='9'>Microsoft.Extensions.Http.Resilience</font> for automatic "
    "retry with exponential backoff on transient HTTP failures. Failed tool calls return structured "
    "error JSON rather than throwing exceptions, allowing the LLM to handle failures gracefully "
    "(e.g., skip a ticker and continue with the rest)."
))
story.append(PageBreak())

# ━━━━━━━━━━━  11. CONFIGURATION  ━━━━━━━━━━━
story.append(h1("11. Configuration Reference"))
story.append(hr())
story.append(body("All configuration lives in <font face='Courier' size='9'>appsettings.json</font> (or environment variables for Docker):"))
story.append(spacer())
story.append(code(
    "{\n"
    '  "AIFoundry": {\n'
    '    "Endpoint": "https://your-resource.openai.azure.com",\n'
    '    "Key": "your-api-key",\n'
    '    "DeploymentName": "gpt-4o",\n'
    '    "UseAzureCredential": false\n'
    "  },\n"
    '  "AlphaVantage": {\n'
    '    "ApiKey": "your-alpha-vantage-key"\n'
    "  }\n"
    "}"
))
story.append(spacer())
story.append(table_block([
    ["Setting", "Required", "Description"],
    ["AIFoundry:Endpoint", "Yes", "Azure OpenAI or compatible endpoint URL"],
    ["AIFoundry:Key", "Conditional", "API key (not needed if UseAzureCredential=true)"],
    ["AIFoundry:DeploymentName", "Yes", "Model deployment name (e.g. gpt-4o)"],
    ["AIFoundry:UseAzureCredential", "No", "Use DefaultAzureCredential (Managed Identity)"],
    ["AlphaVantage:ApiKey", "No", "Alpha Vantage API key (default: 'demo')"],
], col_widths=[2.0*inch, 0.9*inch, 3.1*inch]))
story.append(spacer())

story.append(h2("Docker Compose"))
story.append(code(
    "# Pass config via environment variables:\n"
    "AIFOUNDRY_KEY=your-key docker compose up --build\n"
    "\n"
    "# Or use appsettings.Development.json for local development"
))
story.append(PageBreak())

# ━━━━━━━━━━━  12. QUICK START  ━━━━━━━━━━━
story.append(h1("12. Quick Start"))
story.append(hr())

story.append(h2("Prerequisites"))
story.append(bullet(".NET 9 SDK"))
story.append(bullet("Node.js 22+"))
story.append(bullet("An Azure AI Foundry deployment (or OpenAI-compatible endpoint)"))
story.append(bullet("Alpha Vantage API key (optional — 'demo' key works for limited tickers)"))
story.append(spacer())

story.append(h2("1. Clone and Configure"))
story.append(code(
    "git clone <your-repo-url>\n"
    "cd AgentOpsHub\n"
    "\n"
    "# Edit src/AgentOpsHub.Web/appsettings.Development.json\n"
    "# with your AIFoundry endpoint and key"
))
story.append(spacer())

story.append(h2("2. Run the Backend"))
story.append(code(
    "cd src/AgentOpsHub.Web\n"
    "dotnet run"
))
story.append(spacer())

story.append(h2("3. Run the Frontend"))
story.append(code(
    "cd frontend\n"
    "npm install\n"
    "npm run dev"
))
story.append(spacer())

story.append(h2("4. Try It"))
story.append(body("Open <font face='Courier' size='9'>http://localhost:3000</font> and try these prompts:"))
story.append(spacer())
story.append(bullet('"Scan for earnings surprises" — triggers the Earnings Surprise Scanner'))
story.append(bullet('"Scan $NVDA $TSLA $AAPL" — scans specific tickers'))
story.append(bullet('"What can you help me with?" — tests LLM routing fallback'))
story.append(spacer())

story.append(h2("Composition Root"))
story.append(body("The final Program.cs is 12 lines — thin, declarative, no business logic:"))
story.append(code(
    "using AgentOpsHub.Abstractions;\n"
    "using AgentOpsHub.Core;\n"
    "using MCP.MarketData;\n"
    "using MyFirstAgent;\n"
    "\n"
    "var builder = WebApplication.CreateBuilder(args);\n"
    "\n"
    "builder.Services.AddAgentOpsHub(builder.Configuration);\n"
    "builder.Services.AddMarketDataMcpClient();\n"
    "builder.Services.AddSingleton<IAgentService, EarningsSurpriseScannerAgent>();\n"
    "\n"
    "var app = builder.Build();\n"
    "app.UseAgentOpsHub();\n"
    "app.Run();"
))

story.append(spacer(0.5))
story.append(hr())
story.append(Paragraph(
    "AgentOpsHub — Built with .NET 9, Azure AI Foundry, CopilotKit, and the AG-UI Protocol.",
    styles["Caption"]))

# ── Render ───────────────────────────────────────────────────────────
doc.build(story, onFirstPage=draw_cover, onLaterPages=draw_later_pages)
print(f"PDF generated: {OUTPUT}")
