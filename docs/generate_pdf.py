"""
Generate AI Agent Canvas Guide PDF.
Sections: Overview -> Use Cases -> User Guide -> Developer Guide
"""
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle,
)
from reportlab.lib.enums import TA_CENTER, TA_JUSTIFY

WIDTH, HEIGHT = letter
MARGIN = 0.75 * inch

styles = getSampleStyleSheet()

styles.add(ParagraphStyle('CoverTitle', parent=styles['Title'], fontSize=32, leading=40,
    textColor=HexColor('#1a1a2e'), spaceAfter=12, alignment=TA_CENTER))
styles.add(ParagraphStyle('CoverSubtitle', parent=styles['Normal'], fontSize=14, leading=20,
    textColor=HexColor('#4a4a6a'), alignment=TA_CENTER, spaceAfter=6))
styles.add(ParagraphStyle('SectionTitle', parent=styles['Heading1'], fontSize=24, leading=30,
    textColor=HexColor('#1a1a2e'), spaceBefore=0, spaceAfter=20))
styles.add(ParagraphStyle('ChapterTitle', parent=styles['Heading1'], fontSize=18, leading=24,
    textColor=HexColor('#2d2d4e'), spaceBefore=16, spaceAfter=10))
styles.add(ParagraphStyle('Sub', parent=styles['Heading2'], fontSize=14, leading=18,
    textColor=HexColor('#3d3d5c'), spaceBefore=14, spaceAfter=8))
styles.add(ParagraphStyle('Sub3', parent=styles['Heading3'], fontSize=12, leading=16,
    textColor=HexColor('#4a4a6a'), spaceBefore=10, spaceAfter=6))
styles.add(ParagraphStyle('Body', parent=styles['Normal'], fontSize=10, leading=14,
    alignment=TA_JUSTIFY, spaceAfter=8))
styles.add(ParagraphStyle('CodeBlock', parent=styles['Code'], fontSize=8.5, leading=11,
    backColor=HexColor('#f5f5f5'), borderColor=HexColor('#e0e0e0'),
    borderWidth=0.5, borderPadding=6, spaceAfter=10, leftIndent=12))
styles.add(ParagraphStyle('BulletItem', parent=styles['Normal'], fontSize=10, leading=14,
    leftIndent=24, bulletIndent=12, spaceAfter=4))
styles.add(ParagraphStyle('TocSection', parent=styles['Normal'], fontSize=12, leading=18,
    textColor=HexColor('#1a1a2e'), fontName='Helvetica-Bold', spaceAfter=4, spaceBefore=10))
styles.add(ParagraphStyle('TocItem', parent=styles['Normal'], fontSize=10, leading=15,
    textColor=HexColor('#4a4a6a'), leftIndent=20, spaceAfter=2))

def header_footer(canvas, doc):
    canvas.saveState()
    canvas.setFont('Helvetica', 8)
    canvas.setFillColor(HexColor('#999999'))
    canvas.drawString(MARGIN, 0.5*inch, f"{doc.page}")
    canvas.drawRightString(WIDTH - MARGIN, 0.5*inch, "AI Agent Canvas")
    canvas.restoreState()

def make_table(headers, rows, col_widths=None):
    data = [headers] + rows
    if not col_widths:
        w = (WIDTH - 2*MARGIN - 20) / len(headers)
        col_widths = [w] * len(headers)
    t = Table(data, colWidths=col_widths, repeatRows=1)
    t.setStyle(TableStyle([
        ('BACKGROUND', (0,0), (-1,0), HexColor('#2d2d4e')),
        ('TEXTCOLOR', (0,0), (-1,0), HexColor('#ffffff')),
        ('FONTNAME', (0,0), (-1,0), 'Helvetica-Bold'),
        ('FONTSIZE', (0,0), (-1,0), 9),
        ('FONTSIZE', (0,1), (-1,-1), 9),
        ('ALIGN', (0,0), (-1,-1), 'LEFT'),
        ('VALIGN', (0,0), (-1,-1), 'TOP'),
        ('GRID', (0,0), (-1,-1), 0.5, HexColor('#dddddd')),
        ('ROWBACKGROUNDS', (0,1), (-1,-1), [HexColor('#ffffff'), HexColor('#f9f9f9')]),
        ('TOPPADDING', (0,0), (-1,-1), 6),
        ('BOTTOMPADDING', (0,0), (-1,-1), 6),
        ('LEFTPADDING', (0,0), (-1,-1), 8),
        ('RIGHTPADDING', (0,0), (-1,-1), 8),
    ]))
    return t

def P(text, style='Body'):
    return Paragraph(text, styles[style])

def code(text):
    escaped = text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')
    return Paragraph(escaped, styles['CodeBlock'])

def bullet(text):
    return Paragraph(f"•  {text}", styles['BulletItem'])

def section_page(title, subtitle=""):
    elements = [Spacer(1, 2*inch)]
    elements.append(P(title, 'SectionTitle'))
    if subtitle:
        elements.append(P(subtitle, 'CoverSubtitle'))
    elements.append(PageBreak())
    return elements

story = []

# == COVER PAGE ==
story.append(Spacer(1, 2.5*inch))
story.append(P("AI Agent Canvas", 'CoverTitle'))
story.append(P("Multi-Agent Enterprise Copilot Framework", 'CoverSubtitle'))
story.append(Spacer(1, 0.5*inch))
story.append(P("Build intelligent AI copilots with .NET 9 and CopilotKit.", 'CoverSubtitle'))
story.append(P("Orchestrate specialized agents that reason, plan, and act through a shared tool registry.", 'CoverSubtitle'))
story.append(Spacer(1, 1.5*inch))
story.append(P("Complete Reference Guide", 'CoverSubtitle'))
story.append(PageBreak())

# == TABLE OF CONTENTS ==
story.append(P("Table of Contents", 'SectionTitle'))
story.append(Spacer(1, 12))

toc = [
    ("Overview", [
        "What Are AI Agents", "Microsoft Agent Framework", "Tools and Skills",
        "Model Context Protocol", "Context Providers", "AG-UI Protocol",
        "Notification System"
    ]),
    ("Use Cases", [
        "Financial Services: Market Intelligence Copilot",
        "Healthcare: Clinical Research Assistant",
        "Legal: Contract Review Agent",
        "E-Commerce: Customer Operations Copilot",
        "IT Operations: Incident Response Agent"
    ]),
    ("User Guide", [
        "Getting Started", "Configuration", "Chat Interface", "Personas",
        "Workflows", "Skills and Tools", "Scheduling and Autonomous Execution", "Security"
    ]),
    ("Developer Guide", [
        "Architecture Overview", "Project Structure", "Core Framework",
        "Agent Data", "Skills and MCP", "RAG Pipeline",
        "Inter-Agent Communication", "Autonomous Execution",
        "Security", "Custom Agents", "Custom MCP Connections"
    ]),
]
for section, items in toc:
    story.append(P(section, 'TocSection'))
    for item in items:
        story.append(P(item, 'TocItem'))
story.append(PageBreak())

# ===================================================
# SECTION 1: OVERVIEW
# ===================================================
story.extend(section_page("Overview",
    "Key concepts behind AI agents, the Microsoft Agent Framework, tools, MCP, context providers, and the AG-UI streaming protocol."))

story.append(P("What Are AI Agents", 'ChapterTitle'))
story.append(P("AI agents represent a fundamental shift from traditional chatbots and simple LLM wrappers. Where a chatbot follows scripted flows and an LLM wrapper sends a prompt and returns a response, an AI agent autonomously reasons, plans, and takes action to accomplish goals."))
story.append(Spacer(1, 8))
story.append(P("Chatbots vs LLM Wrappers vs AI Agents", 'Sub'))
story.append(make_table(
    ["Capability", "Traditional Chatbot", "LLM Wrapper", "AI Agent"],
    [
        ["Natural language", "Rule-based / intent matching", "Full LLM capability", "Full LLM capability"],
        ["Decision making", "Scripted decision trees", "Single inference call", "Multi-step reasoning"],
        ["Tool use", "None or hardcoded", "None", "Dynamic tool selection"],
        ["Memory", "Session state only", "Stateless", "Persistent context"],
        ["Autonomy", "None", "None", "Goal-directed behavior"],
    ],
    col_widths=[1.3*inch, 1.5*inch, 1.5*inch, 1.5*inch]
))
story.append(Spacer(1, 8))
story.append(P("Key Characteristics of AI Agents", 'Sub'))
story.append(bullet("<b>Autonomy</b> -- Agents decide which actions to take without step-by-step human instruction. Given a goal, they determine the path."))
story.append(bullet("<b>Tool Use</b> -- Agents interact with external systems through defined tools: APIs, databases, file systems, and other services."))
story.append(bullet("<b>Reasoning</b> -- Agents analyze information, break complex problems into steps, and adapt their approach based on intermediate results."))
story.append(bullet("<b>Memory and Context</b> -- Agents maintain context across interactions including conversation history, retrieved documents (RAG), user preferences, and domain-specific knowledge."))
story.append(Spacer(1, 8))

story.append(P("The Agent Loop", 'Sub'))
story.append(P("Every AI agent follows a fundamental execution cycle: <b>Perceive</b> (receive input, gather context) then <b>Reason</b> (analyze input, plan next step) then <b>Act</b> (call a tool or generate response) then <b>Observe</b> (process result, decide if done). If the goal is not yet achieved, the agent loops back to the Reason step. This loop continues until the agent determines it has enough information to provide a final response."))
story.append(Spacer(1, 8))

story.append(P("Where AI Agent Canvas Fits", 'Sub'))
story.append(P("AI Agent Canvas is a multi-agent enterprise copilot framework built on three pillars:"))
story.append(bullet("<b>Microsoft Agent Framework (MAF)</b> for agent orchestration and the LLM execution loop"))
story.append(bullet("<b>Azure AI Foundry</b> as the LLM provider (GPT-4o, GPT-4.1, and other models)"))
story.append(bullet("<b>CopilotKit with the AG-UI protocol</b> for real-time streaming to the frontend"))
story.append(P("The framework is designed around separation of concerns: Core handles the agent execution loop, tool registry, context providers, and streaming. MCP projects provide data connections and external tool integrations. Custom agents define personas, domain logic, and specialized behavior. The Web project is the composition root that wires everything together."))
story.append(PageBreak())

story.append(P("Microsoft Agent Framework", 'ChapterTitle'))
story.append(P("Microsoft Agent Framework (MAF) is the orchestration layer that powers AI Agent Canvas. It provides the abstractions for building agents that can reason, call tools, and stream responses -- all while integrating with the broader Microsoft.Extensions.AI ecosystem. AI Agent Canvas uses MAF version 1.10.0 from the Microsoft.Agents.AI namespace."))
story.append(Spacer(1, 8))

story.append(P("Core Types", 'Sub'))
story.append(P("<b>IChatClient</b> -- At the foundation, MAF builds on IChatClient from Microsoft.Extensions.AI. This interface represents any LLM provider -- Azure AI Foundry, OpenAI, Ollama, or others. AI Agent Canvas configures it through AIFoundryClientFactory, meaning the framework is not locked to a specific LLM provider."))
story.append(P("<b>ChatClientAgent</b> -- The primary agent implementation. It combines an IChatClient with configuration (tools, context providers, history) and implements the agent loop: receive messages, inject context, call LLM, execute tool calls, repeat until done."))
story.append(P("<b>ChatClientAgentOptions</b> -- Configuration class that defines agent behavior including Name, Description, ChatOptions (with tool list), ChatHistoryProvider, and AIContextProviders chain."))
story.append(P("<b>AIAgent</b> -- Abstract base class for all agents in MAF. ChatClientAgent inherits from it. The AIAgent type is what gets registered in DI and consumed by the AG-UI endpoint."))
story.append(Spacer(1, 8))

story.append(P("The Agent Execution Model", 'Sub'))
story.append(P("When a user sends a message, the following pipeline executes: (1) User message arrives at the AG-UI endpoint. (2) Context providers inject system prompt, persona, guardrails, entity knowledge, RAG results, and governance checks. (3) Messages are assembled with injected context. (4) Tool definitions are attached from the tool registry. (5) The LLM is invoked. (6) If the LLM returns tool calls, the framework executes them and loops back to step 5 with the results. (7) When the LLM returns text instead of tool calls, the response streams back via SSE."))
story.append(Spacer(1, 8))

story.append(P("Middleware Pipeline", 'Sub'))
story.append(P("MAF supports a middleware pattern for cross-cutting concerns. AI Agent Canvas uses this to add logging middleware that records agent invocation timing, message counts, and completion status. The middleware wraps the chat client, intercepting calls to add observability without modifying agent logic."))
story.append(Spacer(1, 8))

story.append(P("Running the Agent", 'Sub'))
story.append(P("MAF provides two execution methods: <b>RunAsync</b> for complete responses (used by scheduled tasks and background jobs) and <b>RunStreamingAsync</b> for SSE streaming (used by the AG-UI endpoint for real-time chat). Both methods participate in the same tool-calling loop; the difference is whether the final text response is returned as a complete string or streamed token-by-token."))
story.append(PageBreak())

story.append(P("Tools and Skills", 'ChapterTitle'))
story.append(P("Tools are how agents interact with the outside world. When the LLM determines it needs external data or wants to perform an action, it requests a tool call. The framework executes the tool and returns the result to the LLM for further reasoning."))
story.append(Spacer(1, 8))

story.append(P("AIFunction and AIFunctionFactory", 'Sub'))
story.append(P("Tools are created using AIFunctionFactory.Create(), which wraps a .NET method as an AITool with a name, description, and parameter schema. The LLM uses the description and schema to decide when and how to call the tool."))
story.append(code('AIFunctionFactory.Create(GetStockQuote, "stock_quote",\n    "Get real-time stock quote including price, volume, and change")'))
story.append(Spacer(1, 6))

story.append(P("The Tool Call Loop", 'Sub'))
story.append(P("When the LLM decides to use a tool: (1) The LLM returns a tool_call with the function name and arguments. (2) The framework looks up the tool in the registry and executes it. (3) The tool result is added to the conversation as a tool response message. (4) The LLM is called again with the updated conversation, allowing it to reason about the result and decide whether to call another tool or generate a final response."))
story.append(Spacer(1, 6))

story.append(P("DynamicToolRegistry", 'Sub'))
story.append(P("A thread-safe registry (ConcurrentDictionary) that enables runtime tool registration and unregistration. Tools are grouped by source key (e.g., 'mcp:weather-api'). This powers MCP connections -- when an agent connects to an MCP server at runtime, the discovered tools are registered under a source key. Disconnecting removes them."))
story.append(Spacer(1, 6))

story.append(P("Tool Providers", 'Sub'))
story.append(P("AI Agent Canvas ships with 70+ registered tools across multiple providers:"))
story.append(make_table(
    ["Provider", "Tools"],
    [
        ["PersonaToolProvider", "create/update/list/switch/read/delete_persona"],
        ["GuardrailToolProvider", "create/update/list/toggle/delete_guardrail"],
        ["WorkflowToolProvider", "create/list/read/run/delete_workflow"],
        ["EntityToolProvider", "save/update/read/search/list/delete_entity"],
        ["GoalToolProvider", "create_goal, list_goals, read_goal, update_goal_status, delete_goal"],
        ["WorkQueueToolProvider", "submit_work_item, list_work_queue, cancel_work_item, get_queue_stats"],
        ["MarketDataToolProvider", "edgar_company_facts, stock_quote, stock_history"],
        ["SkillToolProvider", "create_skill, list_skills, run_skill, remove_skill"],
        ["McpConnectionManager", "connect_mcp_server, disconnect_mcp_server, list_mcp_connections"],
        ["SchedulerToolProvider", "schedule_recurring_task, schedule_one_time_task, list/remove/get_results"],
        ["SchedulerToolProvider (autonomous)", "start_autonomous_mode, stop_autonomous_mode, get_autonomous_status"],
        ["AgentRegistryToolProvider", "list_available_agents, get_agent_info"],
        ["AgentMailboxToolProvider", "send_to_agent, check_inbox, reply_to_message"],
        ["HandoffToolProvider", "handoff_to_agent"],
        ["SystemToolsProvider", "system_read_file, system_write_file, system_list_directory, system_run_script"],
    ],
    col_widths=[2.2*inch, 4.5*inch]
))
story.append(Spacer(1, 6))

story.append(P("Skills: Agent-Managed Tools", 'Sub'))
story.append(P("Skills are reusable prompt templates that the agent can create, store, and execute at runtime. Unlike hardcoded tools, skills are authored through conversation and persisted as markdown files in agent-data/skills/. Each skill has a name, description, and prompt template with an {input} placeholder. When run, the template is populated with the user's input and sent to the LLM."))
story.append(PageBreak())

story.append(P("Model Context Protocol", 'ChapterTitle'))
story.append(P("MCP is an open standard for connecting AI agents to external data sources and tools. Instead of building custom integrations for every data source, MCP provides a standardized protocol for tool discovery and invocation."))
story.append(Spacer(1, 8))

story.append(P("Why MCP Matters", 'Sub'))
story.append(P("Without MCP, every data connection requires custom code: HTTP client setup, response parsing, error handling, and tool registration. MCP standardizes this -- an agent connects to any MCP-compliant server and automatically discovers its available tools, schemas, and capabilities."))
story.append(Spacer(1, 6))

story.append(P("MCP in AI Agent Canvas", 'Sub'))
story.append(P("AI Agent Canvas supports both static MCP connections (configured at startup via IMcpConnectionSeed) and dynamic runtime connections (the agent calls connect_mcp_server during a conversation). McpConnectionManager handles the lifecycle: creating transports (HTTP or SSE), connecting clients, discovering tools via ListToolsAsync, and registering them in the DynamicToolRegistry."))
story.append(Spacer(1, 6))

story.append(P("MCP Tools vs Local Tools", 'Sub'))
story.append(make_table(
    ["Aspect", "Local Tools", "MCP Tools"],
    [
        ["Definition", "Code in the project", "External MCP server"],
        ["Registration", "Startup (DI)", "Runtime (dynamic)"],
        ["Execution", "In-process", "Network call"],
        ["Latency", "Microseconds", "Milliseconds"],
        ["Availability", "Always", "While connected"],
    ],
    col_widths=[1.5*inch, 2.5*inch, 2.5*inch]
))
story.append(Spacer(1, 6))

story.append(P("Security Considerations", 'Sub'))
story.append(P("MCP connections are subject to governance policies via GovernedMcpGateway. This prevents SSRF attacks by blocking connections to private/internal addresses, enforces tool-level access control, and logs all MCP activity for audit. Best practices include endpoint allowlisting, HTTPS enforcement, and timeout configuration."))
story.append(PageBreak())

story.append(P("Context Providers", 'ChapterTitle'))
story.append(P("Context providers are the mechanism for injecting instructions, tools, and knowledge into each LLM call. They form a chain that executes before every agent invocation, assembling the complete context the LLM needs to respond appropriately."))
story.append(Spacer(1, 8))

story.append(P("The Context Injection Chain", 'Sub'))
story.append(P("Eight built-in context providers plus one middleware execute in sequence:"))
story.append(make_table(
    ["Component", "Purpose"],
    [
        ["SystemPromptProvider", "Sets base system instructions"],
        ["PlanningMiddleware", "Goal decomposition: persistent step plans via StateBag"],
        ["PersonaContextProvider", "Appends persona-specific behavior instructions"],
        ["PersistentContextProvider", "Appends typed context entries (fact, reference, decision, feedback)"],
        ["UserProfileContextProvider", "Injects user preferences and information"],
        ["EntityContextProvider", "Appends domain knowledge index"],
        ["GuardrailContextProvider", "Appends behavioral constraints and safety rules"],
        ["RagContextProvider", "Retrieves relevant documents from vector store"],
        ["GovernanceContextProvider", "Scans for prompt injection, emits audit events"],
        ["DynamicToolContextProvider", "Injects runtime tools from DynamicToolRegistry"],
    ],
    col_widths=[2.5*inch, 4.2*inch]
))
story.append(Spacer(1, 6))

story.append(P("Goal Decomposition (PlanningMiddleware)", 'Sub'))
story.append(P("The PlanningMiddleware enables persistent goal decomposition for complex multi-step requests. Implemented as agent middleware (not a context provider), it has access to the session StateBag for plan persistence across messages. On each message, the middleware checks for an existing plan: if none, it makes a lightweight LLM call (MaxOutputTokens=300, Temperature=0) to decide whether a plan is needed. Simple requests get NO_PLAN and pass through unchanged. Complex requests are decomposed into numbered steps referencing available tools. The plan is stored in StateBag and injected as a system message. On subsequent messages, a continuation evaluator checks conversation history to track which steps are complete (COMPLETED/REMAINING/NEXT), clears completed plans (ALL_DONE), or generates a fresh plan if the user changed direction (REPLAN). The planner is aware of both static startup tools and dynamic MCP tools via DynamicToolRegistry."))
story.append(Spacer(1, 6))

story.append(P("Creating Custom Context Providers", 'Sub'))
story.append(P("Custom providers subclass AIContextProvider and override ProvideAIContextAsync. Register them in DI to add domain-specific context injection. Design guidelines: single responsibility per provider, append (don't replace) existing context, fail gracefully, keep instructions concise, and register in logical order."))
story.append(PageBreak())

story.append(P("AG-UI Protocol", 'ChapterTitle'))
story.append(P("AG-UI is the streaming protocol that connects the .NET agent backend (Microsoft Agent Framework) to the CopilotKit frontend over Server-Sent Events (SSE). Instead of returning a complete response in a single payload, AG-UI streams events as the agent processes, enabling token-by-token text rendering."))
story.append(Spacer(1, 8))

story.append(P("The /api/copilotkit Endpoint", 'Sub'))
story.append(P("A single SSE endpoint handles all agent interactions. CopilotKit sends a POST request with a JSON body containing threadId and messages array. The endpoint responds with text/event-stream content type."))
story.append(Spacer(1, 6))

story.append(P("Event Types", 'Sub'))
story.append(make_table(
    ["Event", "Purpose"],
    [
        ["run.started", "Marks agent processing initiation; frontend shows loading indicator"],
        ["text.message.start", "New assistant message; frontend creates message bubble"],
        ["text.message.content", "Text chunk with delta field; frontend appends progressively"],
        ["text.message.end", "Message complete"],
        ["run.finished", "Entire run complete; frontend re-enables input"],
    ],
    col_widths=[2.2*inch, 4.5*inch]
))
story.append(Spacer(1, 6))

story.append(P("Why SSE Over WebSockets", 'Sub'))
story.append(P("SSE is unidirectional (server to client), which matches the agent response pattern perfectly. It works over standard HTTP infrastructure (proxies, CDNs, load balancers) without modification, provides automatic reconnection, and avoids the complexity of WebSocket connection management and heartbeats."))
story.append(Spacer(1, 6))
story.append(P("The endpoint respects the HTTP request's cancellation token -- if the client closes the connection, the RequestAborted token fires, canceling the agent's LLM call and stopping the stream to prevent wasted compute."))
story.append(PageBreak())

story.append(P("Notification System", 'ChapterTitle'))
story.append(P("AI Agent Canvas includes a real-time notification system that delivers scheduled task results and other agent events to the chat interface. Notifications flow through an in-memory channel-based pub/sub (InMemoryNotificationSink) and are delivered to the frontend via a dedicated SSE endpoint at /api/notifications."))
story.append(Spacer(1, 8))

story.append(P("How It Works", 'Sub'))
story.append(P("When a scheduled task completes, ScheduledAgentJob publishes an AgentNotification to the INotificationSink. The NotificationEndpoint reads from this channel and streams each notification as an SSE event with the 'notification' event type. The frontend listens with EventSource and renders each notification as a new assistant message in the chat."))
story.append(Spacer(1, 6))

story.append(P("Architecture", 'Sub'))
story.append(make_table(
    ["Component", "Role"],
    [
        ["INotificationSink", "Abstraction for publishing notifications (in Abstractions)"],
        ["InMemoryNotificationSink", "Channel-based implementation using Channel&lt;AgentNotification&gt;"],
        ["NotificationEndpoint", "SSE endpoint at /api/notifications that streams to browser"],
        ["AgentNotification", "DTO with Title, Body, Source, and Timestamp fields"],
        ["EventSource (frontend)", "Browser API that receives SSE events and updates chat state"],
    ],
    col_widths=[2.5*inch, 4.2*inch]
))
story.append(PageBreak())


# ===================================================
# SECTION 2: USE CASES
# ===================================================
story.extend(section_page("Use Cases",
    "Real-world scenarios showing how AI Agent Canvas applies to financial services, healthcare, legal, e-commerce, and IT operations."))

story.append(P("Use Cases", 'ChapterTitle'))
story.append(P("AI Agent Canvas is a multi-agent enterprise copilot framework that adapts to any industry. The following scenarios demonstrate how organizations use tool-calling agents, MCP data connections, scheduled tasks, skill authoring, personas, guardrails, entity management, user profiles, and security governance to solve real operational challenges. Each use case presents a hypothetical company, a specific persona, the problem they face, and how AI Agent Canvas delivers measurable value."))
story.append(PageBreak())

story.append(P("Financial Services: Market Intelligence Copilot", 'ChapterTitle'))
story.append(P("<b>Company:</b> Meridian Capital -- A mid-size hedge fund ($2.4B AUM) giving junior analysts instant access to market data, SEC filings, and technical indicators while enforcing compliance guardrails."))
story.append(P("<b>Persona:</b> Priya Sharma, Junior Portfolio Analyst"))
story.append(Spacer(1, 8))

story.append(P("The Challenge", 'Sub'))
story.append(P("Priya spends 2-3 hours each morning manually pulling data from Bloomberg, SEC EDGAR, and internal spreadsheets to prepare briefing materials. She needs real-time market data combined with fundamental analysis, but compliance requires that all outputs include proper disclaimers and avoid direct trading recommendations."))
story.append(Spacer(1, 6))

story.append(P("Key Features Demonstrated", 'Sub'))
story.append(bullet("<b>MCP.HelloWorldData tools:</b> stock_quote, edgar_company_facts, stock_technicals for real-time market data and SEC filings"))
story.append(bullet("<b>Scheduled tasks:</b> Morning Watchlist Briefing (6:00 AM daily), Earnings Calendar Alert (weekly Monday)"))
story.append(bullet("<b>Personas:</b> Research Mode (verbose analysis with citations) and Briefing Mode (concise executive bullets)"))
story.append(bullet("<b>Guardrails:</b> NoTradingSignals (blocks buy/sell recommendations), SourceAttribution (requires data citations)"))
story.append(bullet("<b>Skills:</b> Custom prompt templates for earnings analysis and sector comparison"))
story.append(Spacer(1, 6))

story.append(P("Sample Interaction", 'Sub'))
story.append(bullet('"What is the current price and 30-day performance of CAT?"'))
story.append(bullet('"Pull CAT\'s latest 10-K revenue breakdown by segment"'))
story.append(bullet('"Show me the RSI and MACD for CAT over the last 90 days"'))
story.append(bullet('"Create a skill for comparing two stocks side by side"'))
story.append(Spacer(1, 6))

story.append(P("Business Value", 'Sub'))
story.append(P("Morning prep time reduced from 2-3 hours to 15 minutes. Automated watchlist briefings deliver pre-market intelligence before the trading day starts. Compliance guardrails ensure all analyst outputs include required disclaimers."))
story.append(PageBreak())

story.append(P("Healthcare: Clinical Research Assistant", 'ChapterTitle'))
story.append(P("<b>Company:</b> BioNova Therapeutics -- A 90-person biotech startup managing multi-site Phase II clinical trials for three drug candidates."))
story.append(P("<b>Persona:</b> Dr. Marcus Chen, Clinical Research Coordinator"))
story.append(Spacer(1, 8))

story.append(P("The Challenge", 'Sub'))
story.append(P("Dr. Chen coordinates enrollment, adverse events, and endpoint data across 12 trial sites. Data lives in multiple systems, and he needs quick answers about enrollment velocity, overdue visits, and safety signals without waiting for the data team to run custom queries."))
story.append(Spacer(1, 6))

story.append(P("Key Features Demonstrated", 'Sub'))
story.append(bullet("<b>MCP.ClinicalTrials tools:</b> query_enrollment, query_adverse_events, query_endpoints"))
story.append(bullet("<b>Entity Management:</b> Drug compound registry (BNV-204, BNV-118, BNV-330) with trial phase, mechanism, and status"))
story.append(bullet("<b>Personas:</b> Research Mode (exploratory, allows speculation) and Regulatory Mode (strict, citation-required)"))
story.append(bullet("<b>Guardrails:</b> PatientPrivacy (blocks PII), NoDiagnosticClaims (prevents medical conclusions)"))
story.append(bullet("<b>Scheduled tasks:</b> Weekly enrollment dashboard (Monday 6 AM), overdue visit alerts (daily 8 AM)"))
story.append(Spacer(1, 6))

story.append(P("Business Value", 'Sub'))
story.append(P("Enrollment queries that took hours via the data team now complete in seconds. Automated overdue visit detection catches protocol deviations within 24 hours instead of weekly batch reports. Privacy guardrails ensure no patient-identifiable information appears in chat responses."))
story.append(PageBreak())

story.append(P("Legal: Contract Review Agent", 'ChapterTitle'))
story.append(P("<b>Company:</b> Sterling and Associates -- A 120-attorney law firm in Chicago specializing in technology transactions and IP licensing."))
story.append(P("<b>Persona:</b> Elena Vasquez, Associate Attorney"))
story.append(Spacer(1, 8))

story.append(P("The Challenge", 'Sub'))
story.append(P("Elena reviews 15-20 technology contracts monthly, each 50-100 pages. She needs to identify non-standard clauses, compare against firm precedents, and check client-specific requirements. Manual review takes 4-6 hours per contract."))
story.append(Spacer(1, 6))

story.append(P("Key Features Demonstrated", 'Sub'))
story.append(bullet("<b>MCP.DocumentManagement tools:</b> search_precedents, retrieve_clause, check_client_standards"))
story.append(bullet("<b>Custom skills:</b> ClauseExtraction (extract IP assignment clauses), RedlineComparison, PositionReconciliation"))
story.append(bullet("<b>Personas:</b> Associate Mode (detailed analysis with full citations) and Partner Mode (concise executive summary)"))
story.append(bullet("<b>Guardrails:</b> MatterBasedAccessControl, NoLegalAdviceFraming, PrivilegeProtection"))
story.append(bullet("<b>User Profiles:</b> Practice area, seniority level, matter assignments for personalized access"))
story.append(Spacer(1, 6))

story.append(P("Business Value", 'Sub'))
story.append(P("Contract review time reduced from 4-6 hours to 90 minutes. Clause extraction skills ensure consistent identification of non-standard terms. Matter-based access controls prevent cross-client data leakage."))
story.append(PageBreak())

story.append(P("E-Commerce: Customer Operations Copilot", 'ChapterTitle'))
story.append(P("<b>Company:</b> Bloom and Vine -- A D2C home goods company in Portland, OR ($18M annual revenue) with a 3-person customer operations team."))
story.append(P("<b>Persona:</b> Aisha Thompson, Customer Operations Manager"))
story.append(Spacer(1, 8))

story.append(P("The Challenge", 'Sub'))
story.append(P("Aisha's team handles 200+ support tickets weekly across Zendesk, Shopify, and email. Returns processing requires checking order status, verifying return eligibility, creating return labels, and drafting customer responses -- a 15-minute workflow per ticket."))
story.append(Spacer(1, 6))

story.append(P("Key Features Demonstrated", 'Sub'))
story.append(bullet("<b>MCP.Shopify tools:</b> lookup_order, lookup_customer, process_return, check_inventory"))
story.append(bullet("<b>MCP.Zendesk tools:</b> get_ticket, update_ticket, search_tickets, create_macro"))
story.append(bullet("<b>Scheduled tasks:</b> Order Anomaly Scan (2-hourly), Shipping Delay Monitor (4-hourly), Weekly Ticket Trends"))
story.append(bullet("<b>Workflows:</b> ReturnsProcessing (policy check then return creation then label then response draft)"))
story.append(bullet("<b>Guardrails:</b> RequireConfirmation (for destructive actions), SpendLimit ($200 threshold for escalation)"))
story.append(Spacer(1, 6))

story.append(P("Business Value", 'Sub'))
story.append(P("Returns processing drops from 15 minutes to 3 minutes per ticket. Automated anomaly scans catch shipping delays before customers complain. Weekly trend reports identify recurring product issues."))
story.append(PageBreak())

story.append(P("IT Operations: Incident Response Agent", 'ChapterTitle'))
story.append(P("<b>Company:</b> CloudScale Systems -- A B2B SaaS platform with 55 engineers and a 6-person SRE team managing 300+ microservices."))
story.append(P("<b>Persona:</b> Jordan Park, Site Reliability Engineer"))
story.append(Spacer(1, 8))

story.append(P("The Challenge", 'Sub'))
story.append(P("Jordan handles 3-5 incidents per week, each requiring correlation across Datadog metrics, deployment history, and service dependency graphs. Mean time to root cause is 45 minutes, with most time spent context-switching between dashboards."))
story.append(Spacer(1, 6))

story.append(P("Key Features Demonstrated", 'Sub'))
story.append(bullet("<b>MCP.Observability tools:</b> query_metrics, get_alerts, get_deployment_history, query_logs, get_service_dependencies"))
story.append(bullet("<b>Scheduled health checks:</b> SLA Budget (15 min), Deployment Correlation (30 min), Dependency Health (hourly), Certificate Expiry (daily)"))
story.append(bullet("<b>Personas:</b> Incident Mode (terse, action-oriented with escalation thresholds) and Analysis Mode (detailed, trend-focused)"))
story.append(bullet("<b>Guardrails:</b> ReadOnlyDefault, DestructiveActionBlocklist, BlastRadiusAssessment, IncidentEscalationRules"))
story.append(bullet("<b>Entity Management:</b> Service catalog with tier, owner, SLA target, dependencies, and runbook links"))
story.append(Spacer(1, 6))

story.append(P("Business Value", 'Sub'))
story.append(P("Mean time to root cause drops from 45 to 12 minutes. Automated deployment correlation catches bad deploys within the SLA budget check interval. Service catalog entities provide instant context on service ownership and runbooks during incidents."))
story.append(PageBreak())


# ===================================================
# SECTION 3: USER GUIDE
# ===================================================
story.extend(section_page("User Guide",
    "Getting started, configuration, chat interface, personas, skills, scheduling, workflows, and security."))

story.append(P("Getting Started", 'ChapterTitle'))
story.append(P("Prerequisites", 'Sub'))
story.append(bullet(".NET 9 SDK"))
story.append(bullet("Node.js 18+ and npm"))
story.append(bullet("Azure AI Foundry account with a deployed model (GPT-4o or GPT-4.1)"))
story.append(Spacer(1, 6))

story.append(P("Quick Start", 'Sub'))
story.append(code("git clone &lt;your-repo-url&gt; AiAgentCanvas\ncd AiAgentCanvas\ndotnet build AiAgentCanvas.sln\n# Configure appsettings.json with your Azure AI Foundry credentials\ncd src/AiAgentCanvas.Orchestrator\ndotnet run\n# In another terminal:\ncd frontend\nnpm install\nnpm run dev"))
story.append(P("Open the chat interface at <b>http://localhost:5149</b> (backend serves static frontend) or <b>http://localhost:3000</b> (Next.js dev server with API proxy)."))
story.append(Spacer(1, 6))

story.append(P("Your First Conversation", 'Sub'))
story.append(bullet('"What can you help me with?" -- See available capabilities'))
story.append(bullet('"Get me a stock quote for AAPL" -- Test market data tools'))
story.append(bullet('"Create a persona called Analyst focused on data-driven insights" -- Create a persona'))
story.append(bullet('"Schedule a recurring task every hour to check the S&amp;P 500 price" -- Test scheduling'))
story.append(PageBreak())

story.append(P("Configuration", 'ChapterTitle'))
story.append(P("Configuration is managed through appsettings.json in the AiAgentCanvas.Orchestrator project."))
story.append(Spacer(1, 6))

story.append(P("AIFoundry Section", 'Sub'))
story.append(code('{\n  "AIFoundry": {\n    "Endpoint": "https://your-resource.openai.azure.com/",\n    "Key": "your-api-key-here",\n    "DeploymentName": "gpt-4o",\n    "UseAzureCredential": false\n  }\n}'))
story.append(P("Set <b>UseAzureCredential</b> to true for Azure Managed Identity (recommended for production). When enabled, the Key field is ignored and DefaultAzureCredential handles authentication."))
story.append(Spacer(1, 6))

story.append(P("Security Section", 'Sub'))
story.append(code('{\n  "Security": {\n    "PolicyPath": "governance-policy.yaml",\n    "RateLimitPerMinute": 30\n  }\n}'))
story.append(Spacer(1, 6))

story.append(P("Environment Variables", 'Sub'))
story.append(P("All configuration values can be overridden with environment variables using double-underscore notation:"))
story.append(code("AIFoundry__Key=your-key-here\nSecurity__RateLimitPerMinute=60"))
story.append(Spacer(1, 6))

story.append(P("Runtime Data Directories", 'Sub'))
story.append(P("Agent data is stored in markdown files under <b>agent-data/</b> with subdirectories for each domain: personas, context, workflows, entities, profiles, guardrails, goals, and skills. SQLite databases are also created at runtime: workqueue.db (autonomous execution work queue), agentmailbox.db (inter-agent messages), hangfire.db (scheduled tasks), skills.db, chathistory.db, and optionally vectorstore.db."))
story.append(PageBreak())

story.append(P("Chat Interface", 'ChapterTitle'))
story.append(P("The chat interface provides a streaming conversational experience powered by the AG-UI protocol."))
story.append(Spacer(1, 6))
story.append(P("<b>Sending Messages:</b> Type a message and press Enter or click Send. The agent processes your message through the full pipeline: context injection, LLM reasoning, tool calls (if needed), and response streaming."))
story.append(P("<b>Streaming Responses:</b> Responses appear token-by-token as the agent generates them. Tool calls execute in the background -- you will see the agent reason about results and continue generating."))
story.append(P("<b>Notifications:</b> Scheduled tasks publish notifications that appear as chat messages via the SSE notification channel at /api/notifications. When a background task completes, its result is delivered via Server-Sent Events and displayed inline in the conversation."))
story.append(P("<b>Conversation Context:</b> The agent maintains conversation history within a session. Reference earlier messages naturally -- the agent has full context of the conversation."))
story.append(PageBreak())

story.append(P("Personas", 'ChapterTitle'))
story.append(P("Personas define how the agent behaves -- its tone, expertise, focus areas, and communication style. They are injected as system instructions before each LLM call."))
story.append(Spacer(1, 6))
story.append(P("Managing Personas", 'Sub'))
story.append(bullet('"Create a persona called Financial Analyst with instructions to focus on data-driven insights and risk assessment"'))
story.append(bullet('"List my personas" -- See all available personas'))
story.append(bullet('"Switch to the Financial Analyst persona" -- Activate a persona'))
story.append(bullet('"Show me the Financial Analyst persona" -- View full details'))
story.append(bullet('"Update the Financial Analyst persona to also include ESG analysis"'))
story.append(bullet('"Delete the Financial Analyst persona"'))
story.append(Spacer(1, 6))
story.append(P("Personas are stored as markdown files in <b>agent-data/personas/</b> with YAML frontmatter containing name and description, and a body containing the system instructions. The active persona is tracked in <b>agent-data/personas/.active</b>."))
story.append(PageBreak())

story.append(P("Workflows", 'ChapterTitle'))
story.append(P("Workflows are multi-step procedures stored as markdown files. They define a sequence of instructions the agent follows when the workflow is run."))
story.append(Spacer(1, 6))
story.append(P("Managing Workflows", 'Sub'))
story.append(bullet('"Create a workflow called Morning Briefing that gets stock quotes for AAPL, MSFT, and GOOGL, then summarizes market sentiment"'))
story.append(bullet('"List my workflows"'))
story.append(bullet('"Run the Morning Briefing workflow"'))
story.append(bullet('"Delete the Morning Briefing workflow"'))
story.append(Spacer(1, 6))
story.append(P("Workflows are stored in <b>agent-data/workflows/</b> as markdown files with YAML frontmatter (name, description, tags) and step-by-step instructions in the body. They can be combined with scheduling for automated recurring analysis."))
story.append(PageBreak())

story.append(P("Skills and Tools", 'ChapterTitle'))
story.append(P("Skills are reusable prompt templates that you create through conversation. They are persisted as markdown files and can be run with any input."))
story.append(Spacer(1, 6))
story.append(P("Creating and Running Skills", 'Sub'))
story.append(bullet('"Create a skill called summarize-earnings with description Summarize quarterly earnings and template Analyze the following earnings report and provide key highlights: {input}"'))
story.append(bullet('"List my skills" -- See all saved skills'))
story.append(bullet('"Run the summarize-earnings skill with input: [paste earnings data]"'))
story.append(bullet('"Remove the summarize-earnings skill"'))
story.append(Spacer(1, 6))

story.append(P("Skill Authoring (Markdown-Based)", 'Sub'))
story.append(P("Skills can also be authored as markdown files with YAML frontmatter for richer definitions. The SkillAuthoringToolProvider exposes tools for creating, editing, reading, and deleting authored skills directly in the agent-data/skills/ directory."))
story.append(Spacer(1, 6))

story.append(P("Connecting External MCP Servers", 'Sub'))
story.append(bullet('"Connect to an MCP server at https://weather-api.example.com with transport http"'))
story.append(bullet('"List my MCP connections" -- See active connections and tool counts'))
story.append(bullet('"Disconnect from the weather server"'))
story.append(PageBreak())

story.append(P("Scheduling and Autonomous Execution", 'ChapterTitle'))
story.append(P("Scheduled tasks run the AI agent with a given prompt on a cron schedule or after a delay. Tasks are managed by Hangfire with SQLite persistence and survive application restarts. Beyond scheduling, AI Agent Canvas also supports fully autonomous execution where the agent works independently on goals."))
story.append(Spacer(1, 6))

story.append(P("Creating Tasks", 'Sub'))
story.append(bullet('"Schedule a recurring task every day at 9 AM to check the S&amp;P 500 price" (cron: 0 9 * * *)'))
story.append(bullet('"Schedule a one-time task in 30 minutes to analyze AAPL earnings"'))
story.append(Spacer(1, 6))

story.append(P("Common Cron Expressions", 'Sub'))
story.append(make_table(
    ["Schedule", "Cron Expression"],
    [
        ["Every hour", "0 * * * *"],
        ["Every day at 9 AM", "0 9 * * *"],
        ["Every Monday at 8 AM", "0 8 * * 1"],
        ["Every 30 minutes", "*/30 * * * *"],
        ["Every weekday at 6 PM", "0 18 * * 1-5"],
    ],
    col_widths=[3*inch, 3.5*inch]
))
story.append(Spacer(1, 6))

story.append(P("Managing Tasks", 'Sub'))
story.append(bullet('"List my scheduled tasks" -- See all active tasks'))
story.append(bullet('"Get task results" -- View recent completed task outputs'))
story.append(bullet('"Remove scheduled task [task-id]" -- Cancel a task'))
story.append(P("The Hangfire Dashboard is available at <b>/hangfire</b> for visual monitoring of jobs."))
story.append(P("When a scheduled task completes, it publishes a notification via the SSE notification channel. If the chat interface is open, the result appears as a new assistant message in the conversation."))
story.append(Spacer(1, 6))

story.append(P("Autonomous Execution", 'Sub'))
story.append(P("Enable autonomous mode with: <i>\"Start autonomous mode\"</i>. The agent will work independently on goals, polling the work queue and executing items via AIAgent.RunAsync. Create goals with priority and acceptance criteria, submit work items directly, or let the autonomous job decompose goals into work items automatically. Monitor with <i>\"Get autonomous status\"</i> and stop with <i>\"Stop autonomous mode\"</i>. See the Developer Guide for full configuration details."))
story.append(PageBreak())

story.append(P("Security", 'ChapterTitle'))
story.append(P("AI Agent Canvas integrates Microsoft's Agent Governance Toolkit for comprehensive security controls."))
story.append(Spacer(1, 6))

story.append(P("Governance Policy", 'Sub'))
story.append(P("Policies are defined in YAML files that specify allow/deny rules for tools, file paths, and MCP endpoints."))
story.append(code("name: AiAgentCanvas-default\nrules:\n  - name: block-dangerous-tools\n    action: deny\n    tools: [run_script]\n  - name: restrict-file-write-paths\n    action: deny\n    tools: [write_file]\n    paths: [/etc, /var, C:\\Windows]\n  - name: allow-all-other\n    action: allow"))
story.append(Spacer(1, 6))

story.append(P("Security Features", 'Sub'))
story.append(bullet("<b>Rate Limiting:</b> Fixed-window rate limiter (default 30/minute) on the copilotkit endpoint"))
story.append(bullet("<b>Prompt Injection Detection:</b> GovernanceContextProvider scans instructions before each LLM call"))
story.append(bullet("<b>Tool-Call Governance:</b> Policies evaluated before tool execution (allow/deny/audit)"))
story.append(bullet("<b>MCP Gateway:</b> GovernedMcpGateway prevents SSRF and enforces endpoint allowlists"))
story.append(bullet("<b>Security Headers:</b> X-Content-Type-Options, X-Frame-Options, Referrer-Policy"))
story.append(bullet("<b>Audit Logging:</b> All governance decisions are logged for compliance review"))
story.append(Spacer(1, 6))

story.append(P("Guardrails (User-Defined Safety Rules)", 'Sub'))
story.append(P("Guardrails are runtime-configurable behavioral constraints injected into the system prompt. Create them through conversation:"))
story.append(bullet('"Create a guardrail called no-trading-signals with rule: Never provide buy, sell, or hold recommendations"'))
story.append(bullet('"Toggle the no-trading-signals guardrail" -- Enable/disable'))
story.append(bullet('"List my guardrails" -- See all active rules'))
story.append(Spacer(1, 6))

story.append(P("Production Security Checklist", 'Sub'))
story.append(bullet("Use Managed Identity instead of API keys"))
story.append(bullet("Customize governance-policy.yaml for your domain"))
story.append(bullet("Add authentication to the Hangfire dashboard"))
story.append(bullet("Enable HTTPS/TLS"))
story.append(bullet("Set AllowedHosts to your specific domain"))
story.append(bullet("Review audit logs regularly"))
story.append(PageBreak())


# ===================================================
# SECTION 4: DEVELOPER GUIDE
# ===================================================
story.extend(section_page("Developer Guide",
    "Architecture, project structure, core framework internals, agent data domains, skills, MCP, security, and custom agent development."))

story.append(P("Architecture Overview", 'ChapterTitle'))
story.append(P("AI Agent Canvas is a .NET 9 multi-agent copilot framework built on Microsoft's Agent Framework (MAF). It connects a Next.js frontend to Azure AI Foundry through the AG-UI streaming protocol, with a modular tool registry and governance layer."))
story.append(Spacer(1, 8))

story.append(P("Request Flow", 'Sub'))
story.append(P("Browser User sends message via CopilotKit Frontend, which POSTs to /api/copilotkit. The AgUiEndpoint receives the request and passes it to ChatClientAgent. Context Providers inject system prompt, persona, guardrails, entity knowledge, RAG results, and governance checks. The agent calls Azure AI Foundry LLM, executes tool calls in a loop, and streams the response back via SSE to the Frontend UI."))
story.append(Spacer(1, 6))

story.append(P("Framework vs Custom Separation", 'Sub'))
story.append(P("The solution enforces a strict boundary: SDK projects (src/AiAgentCanvas/*) handle orchestration, tools, and persistence; the Orchestrator (src/Orchestrator/) is the composition root; agent projects (src/Agents/) contain business-specific personas and seeds; and data connections (src/DataConnections/) provide external tool integrations. Custom projects never reference framework internals -- they register tools and prompts through DI, and the framework wires everything together. Agents start in-process but are designed to separate via IAgentMessaging."))
story.append(Spacer(1, 6))

story.append(P("Key Packages", 'Sub'))
story.append(make_table(
    ["Package", "Version", "Purpose"],
    [
        ["Microsoft.Agents.AI", "1.10.0", "Agent orchestration (ChatClientAgent, AIAgent)"],
        ["Microsoft.Extensions.AI", "10.7.0", "IChatClient, AITool, embeddings abstraction"],
        ["ModelContextProtocol", "1.4.0", "MCP client for external tool connections"],
        ["AgentGovernance", "Latest", "Policy engine, prompt injection, audit"],
        ["Azure.AI.OpenAI", "2.2.0", "Azure AI Foundry client"],
        ["Hangfire", "1.8+", "Background job scheduling"],
    ],
    col_widths=[2.2*inch, 0.8*inch, 3.5*inch]
))
story.append(Spacer(1, 6))

story.append(P("Extension Method Pattern", 'Sub'))
story.append(P("Every module follows the same DI registration pattern: an AddAiAgentCanvas*() extension method on IServiceCollection that registers stores, tool providers, context providers, and tools as IReadOnlyList&lt;AITool&gt; singletons. The composition root (Program.cs) calls these methods to assemble the application."))
story.append(PageBreak())

story.append(P("Project Structure", 'ChapterTitle'))
story.append(P("Solution Layout", 'Sub'))
story.append(make_table(
    ["Project", "Purpose"],
    [
        ["AiAgentCanvas/ (SDK)", ""],
        ["  .Abstractions", "Shared interfaces, seed contracts, IAgentMessaging, MarkdownFile"],
        ["  .Core", "Agent orchestration, AG-UI endpoint, tool registry, InProcessAgentMessaging"],
        ["  .AgentData", "Seven data domains: personas, context, workflows, entities, profiles, guardrails, goals"],
        ["  .Skills", "Skill store, MCP connections, skill registry, skill authoring"],
        ["  .Scheduler", "Hangfire job scheduling, task store, autonomous execution engine"],
        ["  .Notifications", "In-memory notification sink with SSE endpoint"],
        ["  .Security", "Agent Governance integration, rate limiting, security headers"],
        ["  .SystemTools", "Sandboxed file I/O and shell execution"],
        ["Orchestrator/", ""],
        ["  AiAgentCanvas.Orchestrator", "Composition root (Program.cs), static frontend hosting"],
        ["Agents/", ""],
        ["  Agent.HelloWorld", "Example agent with full seed implementation"],
        ["DataConnections/", ""],
        ["  MCP.HelloWorldData", "Yahoo Finance and SEC EDGAR tool providers"],
        ["  VectorStore.Sqlite", "SQLite-backed vector store for RAG embeddings"],
    ],
    col_widths=[2.5*inch, 4.2*inch]
))
story.append(Spacer(1, 6))

story.append(P("Dependency Flow", 'Sub'))
story.append(P("All SDK projects depend downward to Abstractions. The Orchestrator references all SDK, Agent, and DataConnection projects. Agent and DataConnection projects reference only Abstractions -- never framework internals. IAgentMessaging in Abstractions is the separation seam: swap InProcessAgentMessaging for gRPC/queue to make agents independent services."))
story.append(PageBreak())

story.append(P("Core Framework", 'ChapterTitle'))
story.append(P("The Core project is the central orchestration engine."))
story.append(Spacer(1, 6))

story.append(P("ServiceCollectionExtensions.AddAiAgentCanvas()", 'Sub'))
story.append(P("Registers: AiAgentCanvasOptions, AIFoundryClientFactory, DynamicToolRegistry, all context providers, ChatClientAgent (via AIAgentBuilder), CORS policy, and tool dependency validation."))
story.append(Spacer(1, 6))

story.append(P("Tool Registration: Two Paths", 'Sub'))
story.append(P("<b>Startup registration:</b> Modules register IReadOnlyList&lt;AITool&gt; as singleton services. Core collects all of these and passes them to ChatClientAgentOptions.ChatOptions.Tools."))
story.append(P("<b>Runtime registration:</b> DynamicToolRegistry allows tools to be added/removed at runtime. DynamicToolContextProvider merges these into ChatOptions before each LLM call."))
story.append(Spacer(1, 6))

story.append(P("Tool Dependency Validation", 'Sub'))
story.append(P("At startup, Core resolves all IToolDependencySeed services and validates that the required tools are registered. Missing tools are logged as warnings, allowing the application to start while surfacing configuration issues."))
story.append(code("foreach (var dep in sp.GetServices&lt;IToolDependencySeed&gt;())\n{\n    var missing = dep.RequiredTools\n        .Where(t =&gt; !toolNames.Contains(t)).ToList();\n    if (missing.Count &gt; 0)\n        logger.LogWarning(\n            \"Agent '{Agent}' requires tools: {Missing}\",\n            dep.AgentName, string.Join(\", \", missing));\n}"))
story.append(PageBreak())

story.append(P("Agent Data", 'ChapterTitle'))
story.append(P("Agent data is organized into seven domains, each following the same three-class pattern: Store (CRUD on markdown files), ToolProvider (LLM-callable tools), and ContextProvider (injection before each LLM call). The Goals domain additionally includes a SQLite-backed work queue for autonomous execution."))
story.append(Spacer(1, 8))

story.append(P("The Seven Domains", 'Sub'))
story.append(make_table(
    ["Domain", "Storage Path", "Context Injection"],
    [
        ["Personas", "agent-data/personas/", "Active persona instructions appended to system prompt"],
        ["Context", "agent-data/context/", "Typed context (fact, reference, decision, feedback) grouped and appended"],
        ["Workflows", "agent-data/workflows/", "Available as tools (no context injection)"],
        ["Entities", "agent-data/entities/", "Entity index appended as domain knowledge"],
        ["User Profiles", "agent-data/profiles/", "Active profile preferences injected for personalization"],
        ["Guardrails", "agent-data/guardrails/", "Enabled rules appended as behavioral constraints"],
        ["Goals", "agent-data/goals/", "Managed via tools; drives autonomous execution job"],
    ],
    col_widths=[1.3*inch, 1.8*inch, 3.5*inch]
))
story.append(Spacer(1, 6))

story.append(P("MarkdownFile: The Persistence Foundation", 'Sub'))
story.append(P("All agent data uses the same file format: YAML frontmatter (delimited by ---) containing metadata, followed by markdown body content. MarkdownFile provides Parse(), Write(), LoadAll(), and SanitizeFileName() methods. This approach makes all agent data human-readable, version-controllable, and editable outside the application."))
story.append(code("---\nname: financial-analyst\ndescription: Data-driven market analysis persona\n---\nYou are a financial analyst. Focus on quantitative data,\nrisk assessment, and actionable insights."))
story.append(Spacer(1, 6))

story.append(P("Goals Domain", 'Sub'))
story.append(P("The Goals domain provides markdown-persisted goal definitions and a SQLite-backed transient work queue. Goals have name, description, priority (critical/high/medium/low), status, acceptance criteria, and optional assigned agent. The GoalStore follows the same agent/user directory split as other domains. The WorkQueueStore (workqueue.db) manages individual work items with priority-ordered claiming via atomic SQL."))
story.append(Spacer(1, 6))

story.append(P("Creating a New Domain", 'Sub'))
story.append(P("To add a new domain: (1) Create a subdirectory in agent-data/. (2) Create a Store class with CRUD methods using MarkdownFile. (3) Create a ToolProvider with AIFunctionFactory tools. (4) Optionally create a ContextProvider. (5) Create an AddAiAgentCanvas*() extension method. (6) Register in Program.cs."))
story.append(PageBreak())

story.append(P("Skills and MCP", 'ChapterTitle'))
story.append(P("The Skills project provides four components: SkillStore (CRUD for prompt templates), McpConnectionManager (runtime MCP connections), LocalSkillRegistry (skill catalog), and SkillAuthoringToolProvider (markdown-based skill creation)."))
story.append(Spacer(1, 8))

story.append(P("SkillStore", 'Sub'))
story.append(P("Skills are persisted as markdown files in agent-data/skills/ using the same MarkdownFile format as all other agent data domains. Each skill has a name, description, and prompt template with {input} placeholder in the markdown body."))
story.append(Spacer(1, 6))

story.append(P("McpConnectionManager", 'Sub'))
story.append(P("Manages runtime MCP connections. When the agent calls connect_mcp_server: (1) Creates an HttpClientTransport or SseClientTransport. (2) Connects via McpClient. (3) Discovers tools via ListToolsAsync. (4) Registers tools in DynamicToolRegistry under 'mcp:name' key. Disconnecting removes the tools and disposes the client."))
story.append(Spacer(1, 6))

story.append(P("IMcpConnectionSeed: Static Connections", 'Sub'))
story.append(P("For MCP connections that should be established at application startup (rather than dynamically by the agent), implement IMcpConnectionSeed. This interface declares Name, Endpoint, and Transport. At startup, the framework resolves all IMcpConnectionSeed instances and connects to them automatically, registering their tools in the DynamicToolRegistry."))
story.append(Spacer(1, 6))

story.append(P("MCP Connection Lifecycle", 'Sub'))
story.append(P("MCP connections are subject to governance policies via GovernedMcpGateway. The gateway can block connections to private/internal addresses (SSRF prevention), require tool approval, and log all activity for audit."))
story.append(PageBreak())

# RAG Pipeline
story.append(P("RAG Pipeline", 'ChapterTitle'))
story.append(P("The RAG (Retrieval-Augmented Generation) pipeline enriches agent responses with relevant documents from a vector store. It goes beyond basic vector search with recursive chunking, hybrid search, metadata filtering, LLM-based reranking, and citation attribution."))
story.append(Spacer(1, 8))

story.append(P("Pipeline Stages", 'Sub'))
story.append(make_table(
    ["Stage", "Component", "Description"],
    [
        ["1. Chunking", "DocumentChunker", "Recursive split: paragraphs then sentences, with overlap"],
        ["2. Embedding", "IEmbeddingGenerator", "Azure AI Foundry text-embedding model"],
        ["3. Storage", "SqliteDocumentCollection", "Vector BLOB + FTS5 full-text index"],
        ["4. Hybrid Search", "IHybridSearchable", "70% cosine similarity + 30% BM25 keyword"],
        ["5. Filtering", "RagSearchOptions", "Source (exact) and Tags (contains) SQL filters"],
        ["6. Reranking", "LlmReranker", "LLM rescores top-10 candidates, keeps top-3"],
        ["7. Citation", "RagContextProvider", "Numbered citations with source and score"],
    ],
    col_widths=[1.2*inch, 1.8*inch, 3.6*inch]
))
story.append(Spacer(1, 6))

story.append(P("Document Chunking", 'Sub'))
story.append(P("The DocumentChunker in Core recursively splits documents into chunks suitable for embedding. It splits by paragraphs first (double newlines), then by sentences if a paragraph exceeds ChunkSize (default 512 chars). Configurable overlap (default 64 chars) provides context continuity between consecutive chunks. Chunks under 20 characters are discarded."))
story.append(Spacer(1, 6))

story.append(P("Hybrid Search", 'Sub'))
story.append(P("The SQLite vector store maintains both a main table (with embedding BLOBs) and an FTS5 full-text index. At query time, both cosine similarity and BM25 keyword scores are computed and combined: finalScore = (vectorWeight * cosineSimilarity) + (keywordWeight * bm25Score). Default weights are 70% vector, 30% keyword. The IHybridSearchable interface in Abstractions keeps Core decoupled from the SQLite implementation."))
story.append(Spacer(1, 6))

story.append(P("Metadata Filtering", 'Sub'))
story.append(P("DocumentRecord includes Source (e.g., 'annual-report-2024.pdf') and Tags (e.g., 'finance,quarterly') fields. RagSearchOptions supports SourceFilter (exact match) and TagFilter (LIKE match) applied as SQL WHERE clauses before vector scoring, reducing the candidate set."))
story.append(Spacer(1, 6))

story.append(P("LLM-Based Reranking", 'Sub'))
story.append(P("After retrieval, LlmReranker sends the query and top-10 candidates (truncated to 300 chars each) to the LLM, asking it to return a JSON array ranking them by relevance. The top-3 from the reranked list are kept. This cross-encoder pattern produces better relevance judgments than embedding similarity alone. Uses MaxOutputTokens=100 and Temperature=0 for speed and determinism. Falls back to original ranking on any error."))
story.append(Spacer(1, 6))

story.append(P("Citation and Attribution", 'Sub'))
story.append(P("RagContextProvider formats each result as a numbered citation: [1] (source: filename.pdf, score: 0.892) followed by a text preview. The LLM is instructed to 'cite by number when using' these documents, producing responses like: 'Revenue grew 12% to $4.2B [1], with management focusing on AI infrastructure [2].'"))
story.append(Spacer(1, 6))

story.append(P("Architecture", 'Sub'))
story.append(make_table(
    ["Component", "Location", "Purpose"],
    [
        ["DocumentRecord", "Abstractions", "DTO with Id, Text, Source, Tags, Embedding"],
        ["DocumentChunk", "Abstractions", "Chunker output: Text, Index, Source"],
        ["RagSearchOptions", "Abstractions", "Filters and weights for hybrid search"],
        ["IHybridSearchable", "Abstractions", "Interface for hybrid vector + keyword search"],
        ["DocumentChunker", "Core", "Recursive paragraph/sentence text splitter"],
        ["LlmReranker", "Core", "LLM-based candidate reranking"],
        ["RagContextProvider", "Core", "Orchestrates search, rerank, citation, injection"],
        ["SqliteDocumentCollection", "VectorStore.Sqlite", "SQLite + FTS5 hybrid search implementation"],
    ],
    col_widths=[2*inch, 1.5*inch, 3.2*inch]
))
story.append(PageBreak())

# Inter-Agent Communication
story.append(P("Inter-Agent Communication", 'ChapterTitle'))
story.append(P("AI Agent Canvas supports multi-agent collaboration through three mechanisms registered via AddAiAgentCanvasInterAgentCommunication() in Core."))
story.append(Spacer(1, 8))

story.append(P("Agent Registry", 'Sub'))
story.append(P("AgentRegistry builds and caches named ChatClientAgent instances from persona definitions. Each persona becomes a resolvable agent with its own instructions, sharing the same tools and context providers as the main agent. The registry uses a ConcurrentDictionary cache and delegate-based persona lookup to avoid circular project dependencies (Core cannot reference AgentData)."))
story.append(Spacer(1, 6))

story.append(P("Agent Mailbox", 'Sub'))
story.append(P("AgentMailbox is a SQLite-backed per-agent message queue (agentmailbox.db) for asynchronous communication. Messages have sender, recipient, content, status (pending/read/replied), and optional response. Tools: send_to_agent, check_inbox, reply_to_message."))
story.append(Spacer(1, 6))

story.append(P("Handoff (Synchronous Delegation)", 'Sub'))
story.append(P("The handoff_to_agent tool runs a target agent synchronously using AIAgent.RunAsync and returns the result to the calling agent in the same turn. This enables one agent to delegate a subtask to a specialist agent and reason about its response before replying to the user."))
story.append(Spacer(1, 6))

story.append(P("Inter-Agent Tools", 'Sub'))
story.append(make_table(
    ["Tool", "Description"],
    [
        ["list_available_agents", "List all agents available for handoff or messaging"],
        ["get_agent_info", "Get details about a specific agent (persona name, description)"],
        ["handoff_to_agent", "Synchronous delegation: run target agent and return result"],
        ["send_to_agent", "Send async message to another agent's mailbox"],
        ["check_inbox", "Check for pending messages from other agents"],
        ["reply_to_message", "Reply to a message from another agent"],
    ],
    col_widths=[2.2*inch, 4.5*inch]
))
story.append(PageBreak())

# Autonomous Execution
story.append(P("Autonomous Execution", 'ChapterTitle'))
story.append(P("The Scheduler project includes an autonomous execution engine that allows the agent to work independently on goals without user input."))
story.append(Spacer(1, 8))

story.append(P("How It Works", 'Sub'))
story.append(P("When autonomous mode is enabled, a Hangfire recurring job (AutonomousAgentJob) runs on a configurable schedule. Each run: (1) Polls WorkQueueStore.ClaimNext() for pending work items, prioritized by urgency. (2) If no work items exist, picks the next active goal (sorted by priority) that doesn't already have pending work items. (3) Creates a work item from the goal and claims it. (4) Executes the work item via AIAgent.RunAsync with a constructed prompt. (5) Saves the result and sends a notification. (6) Repeats up to MaxIterationsPerRun (default 5) per job execution."))
story.append(Spacer(1, 6))

story.append(P("Configuration", 'Sub'))
story.append(make_table(
    ["Option", "Default", "Description"],
    [
        ["Enabled", "false", "Whether autonomous mode is active"],
        ["MaxIterationsPerRun", "5", "Max work items processed per job run"],
        ["PollIntervalSeconds", "30", "Seconds between polls when queue is empty"],
        ["CronExpression", "*/30 * * * * *", "Hangfire cron schedule for the recurring job"],
    ],
    col_widths=[2*inch, 1.2*inch, 3.5*inch]
))
story.append(Spacer(1, 6))

story.append(P("Goal and Work Queue Tools", 'Sub'))
story.append(make_table(
    ["Tool", "Description"],
    [
        ["create_goal", "Create a new goal with name, description, priority, acceptance criteria"],
        ["list_goals", "List all goals, optionally filtered by status"],
        ["read_goal", "Read full details of a goal"],
        ["update_goal_status", "Change goal status (active/completed/paused/cancelled)"],
        ["delete_goal", "Delete a goal"],
        ["submit_work_item", "Submit a work item to the queue with priority"],
        ["list_work_queue", "List items in the work queue"],
        ["cancel_work_item", "Cancel a pending work item"],
        ["get_queue_stats", "Get work queue statistics (pending, claimed, completed counts)"],
    ],
    col_widths=[2.2*inch, 4.5*inch]
))
story.append(Spacer(1, 6))

story.append(P("Autonomous Mode Tools", 'Sub'))
story.append(make_table(
    ["Tool", "Description"],
    [
        ["start_autonomous_mode", "Enable autonomous execution, register Hangfire recurring job"],
        ["stop_autonomous_mode", "Disable autonomous execution, remove the recurring job"],
        ["get_autonomous_status", "Check whether autonomous mode is currently enabled"],
    ],
    col_widths=[2.2*inch, 4.5*inch]
))
story.append(PageBreak())

story.append(P("Security", 'ChapterTitle'))
story.append(P("Security is implemented through Microsoft's Agent Governance Toolkit, integrated via AddAiAgentCanvasSecurity()."))
story.append(Spacer(1, 8))

story.append(P("What Gets Registered", 'Sub'))
story.append(make_table(
    ["Component", "Purpose"],
    [
        ["GovernanceKernel", "Central governance object with PolicyEngine, AuditEmitter, InjectionDetector"],
        ["GovernanceContextProvider", "Pre-LLM scanning for prompt injection in system instructions"],
        ["GovernedMcpGateway", "Evaluates tool calls against governance policies (allow/deny)"],
        ["IToolGovernanceWrapper", "Wraps tools to enforce governance at execution time"],
        ["ASP.NET Rate Limiter", "Fixed-window rate limiting (default 30/minute)"],
        ["Security Headers Middleware", "X-Content-Type-Options, X-Frame-Options, Referrer-Policy"],
    ],
    col_widths=[2.5*inch, 4.2*inch]
))
story.append(Spacer(1, 6))

story.append(P("Governance Policy (YAML)", 'Sub'))
story.append(P("Rules specify name, scope, action (allow/deny), conditions (equals, in, matches), and reason. The ConflictStrategy (default: DenyOverrides) determines behavior when multiple rules match."))
story.append(PageBreak())

story.append(P("Adding Custom Agents", 'ChapterTitle'))
story.append(P("Custom agents are lightweight projects under src/Agents/ that define specialized behavior through the seed interface pattern. Seeds declare the agent's components -- persona, context, workflows, entities, guardrails, goals, skills, MCP connections, and tool dependencies. The framework resolves and persists them at startup. Agents start in-process (inside the Orchestrator) but can be separated into independent services by swapping InProcessAgentMessaging for a network transport."))
story.append(Spacer(1, 8))

story.append(P("The Seed Interface Pattern", 'Sub'))
story.append(P("Nine seed interfaces in Abstractions allow custom agents to declaratively register all their components:"))
story.append(make_table(
    ["Interface", "Purpose", "Key Fields"],
    [
        ["IPersonaSeed", "Agent identity and instructions", "AgentName, Description, Instructions"],
        ["IContextSeed", "Typed domain knowledge (fact, reference, decision, feedback)", "Topic, Type, Tags, Content"],
        ["IWorkflowSeed", "Multi-step procedures", "Name, Description, Tags, Content"],
        ["IEntitySeed", "Schema/reference definitions", "Name, Type, Tags, Content"],
        ["IGuardrailSeed", "Safety rules and constraints", "Name, Severity, Enabled, Rule"],
        ["IGoalSeed", "Autonomous execution goals", "Name, Description, Priority, Content"],
        ["ISkillSeed", "Reusable prompt templates", "Name, Description, PromptTemplate"],
        ["IMcpConnectionSeed", "External MCP server connections", "Name, Endpoint, Transport"],
        ["IToolDependencySeed", "Required tool declarations", "AgentName, RequiredTools"],
    ],
    col_widths=[1.6*inch, 2*inch, 3*inch]
))
story.append(Spacer(1, 6))

story.append(P("Seeds never overwrite manual edits -- they only create components if they don't already exist on disk. This means you can seed defaults and then customize them through the chat interface or by editing the markdown files directly."))
story.append(Spacer(1, 8))

story.append(P("Step-by-Step Guide", 'Sub'))
story.append(P("<b>Step 1:</b> Create a project folder under src/Agents/ (e.g., MyAgent)"))
story.append(P("<b>Step 2:</b> Create a .csproj targeting net9.0 with a reference to AiAgentCanvas.Abstractions"))
story.append(P("<b>Step 3:</b> Create a ServiceExtensions class that registers seed implementations:"))
story.append(code("public static class MyAgentServiceExtensions\n{\n    public static IServiceCollection AddMyAgent(\n        this IServiceCollection services)\n    {\n        services.AddSingleton&lt;IPersonaSeed&gt;(\n            new PersonaSeed\n            {\n                AgentName = \"my-agent\",\n                Description = \"Domain expert\",\n                Instructions = \"You are a domain expert...\"\n            });\n        services.AddSingleton&lt;IGuardrailSeed&gt;(\n            new GuardrailSeed\n            {\n                Name = \"safety-rule\",\n                Severity = \"high\",\n                Enabled = true,\n                Rule = \"Never disclose internal data\"\n            });\n        services.AddSingleton&lt;IToolDependencySeed&gt;(\n            new ToolDependencySeed(\"my-agent\",\n                new[] { \"tool_a\", \"tool_b\" }));\n        return services;\n    }\n}"))
story.append(P("<b>Step 4:</b> Add ProjectReference in AiAgentCanvas.Orchestrator.csproj"))
story.append(P("<b>Step 5:</b> Add to solution: dotnet sln add src/Agents/MyAgent"))
story.append(P("<b>Step 6:</b> Wire up in Program.cs: builder.Services.AddMyAgent();"))
story.append(Spacer(1, 8))

story.append(P("HelloWorldAgent Reference", 'Sub'))
story.append(P("The built-in HelloWorldAgent demonstrates a complete seed implementation with all component types:"))
story.append(make_table(
    ["Component", "Seed Value"],
    [
        ["Persona", "financial-analyst -- Quantitative market analysis expert"],
        ["Guardrail", "investment-disclaimer -- Include disclaimers, never give direct advice"],
        ["Workflow", "full-stock-analysis -- Multi-step stock analysis procedure"],
        ["Context", "financial-analysis-methodology -- Fundamental and technical analysis guide"],
        ["Entity", "stock-analysis-report -- Template for structured analysis output"],
        ["Skill", "compare-stocks -- Side-by-side stock comparison template"],
        ["Tool Dependencies", "stock_quote, stock_history, edgar_company_facts"],
    ],
    col_widths=[1.5*inch, 5.2*inch]
))
story.append(PageBreak())

story.append(P("Adding Custom MCP Connections", 'ChapterTitle'))
story.append(P("Custom MCP projects provide tools (data connections, APIs) as class libraries. They use AIFunctionFactory.Create() to wrap .NET methods as LLM-callable tools and register them via IReadOnlyList&lt;AITool&gt;."))
story.append(Spacer(1, 8))

story.append(P("Step-by-Step Guide", 'Sub'))
story.append(P("<b>Step 1:</b> Create a project under src/Agents/ with Microsoft.Extensions.AI and optional HTTP packages"))
story.append(P("<b>Step 2:</b> Create a ToolProvider class with methods decorated with [Description] attributes"))
story.append(P("<b>Step 3:</b> Create ServiceExtensions registering HttpClient, ToolProvider, and IReadOnlyList&lt;AITool&gt;"))
story.append(P("<b>Step 4:</b> Wire up in Program.cs"))
story.append(Spacer(1, 6))

story.append(P("MCP.HelloWorldData Reference", 'Sub'))
story.append(P("The built-in MCP.HelloWorldData project demonstrates the pattern with three tools:"))
story.append(bullet("<b>edgar_company_facts</b> -- SEC EDGAR API for company financial data"))
story.append(bullet("<b>stock_quote</b> -- Yahoo Finance for real-time stock prices"))
story.append(bullet("<b>stock_history</b> -- Historical price data"))
story.append(P("It registers two named HttpClients ('SEC' and 'Yahoo') with timeouts and AddStandardResilienceHandler() for retry policies (Polly-based). The retry handler automatically recovers from transient connection errors like stale HTTPS connections and socket resets."))
story.append(Spacer(1, 6))

story.append(P("Tool Design Guidelines", 'Sub'))
story.append(bullet("Return JSON strings from tool methods"))
story.append(bullet("Handle errors gracefully -- return error JSON instead of throwing"))
story.append(bullet("Accept and pass CancellationToken"))
story.append(bullet("Use descriptive snake_case names (stock_quote, not StockQuote)"))
story.append(bullet("Keep tools focused -- one action per tool"))
story.append(bullet("Document parameters with [Description] attributes"))
story.append(bullet("Log execution timing for observability"))

# Build the PDF
doc = SimpleDocTemplate(
    r"D:\personal\AiAgentCanvas\docs\guides\AI-Agent-Canvas-Guide.pdf",
    pagesize=letter,
    leftMargin=MARGIN, rightMargin=MARGIN,
    topMargin=MARGIN, bottomMargin=MARGIN,
    title="AI Agent Canvas - Complete Reference Guide",
    author="AI Agent Canvas"
)
doc.build(story, onFirstPage=header_footer, onLaterPages=header_footer)
print(f"PDF generated: {doc.filename}")
print(f"Pages: {doc.page}")
