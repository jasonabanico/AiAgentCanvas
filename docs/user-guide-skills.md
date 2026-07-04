> [User Guide](user-guide.md) > Skills & Tools

# User Guide: Skills & Tools

AI Agent Canvas comes with built-in tools and lets you create custom skills and connect external data sources via MCP. This page covers all three.

## Built-In Tools

The following sample tools are included when the MarketData MCP module is enabled:

| Tool | Description |
|------|-------------|
| `stock_quote` | Fetches the current stock price and key metrics for a given ticker symbol. |
| `edgar_company_facts` | Retrieves SEC EDGAR filing data for a company. |
| `stock_history` | Retrieves historical stock data from Yahoo Finance with a configurable range parameter. |

Use them by asking the agent in natural language:

```
Get a stock quote for AAPL
Show me the EDGAR company facts for Microsoft
Show me the stock history for TSLA over the last 6 months
```

The agent selects the appropriate tool, calls it, and incorporates the results into its response.

## What Are Skills?

Skills are user-defined tool definitions that extend the agent's capabilities. Unlike built-in tools, which are compiled into the application, skills are created at runtime through conversation.

Each skill has:

- **Name** -- A short identifier.
- **Description** -- What the skill does.
- **Prompt template** -- Instructions the agent follows when the skill is invoked.

When you run a skill, the agent executes the prompt template through the LLM, producing a result based on the skill's instructions.

## Creating Skills

Ask the agent to create a skill:

```
Create a skill called "Market Summary" that analyzes the current prices of
AAPL, MSFT, GOOGL, and AMZN and produces a one-paragraph market summary.
```

The agent calls `create_skill` and stores the skill in a SQLite database (`skills.db`).

## Listing and Running Skills

List your skills:

```
List my skills
```

Run a skill by name:

```
Run the Market Summary skill
```

The agent calls `run_skill`, which executes the skill's prompt template through the LLM and returns the result.

## Removing Skills

```
Remove the Market Summary skill
```

## Skill Authoring (Markdown-Based)

For more structured skill definitions, use the skill authoring system. Authored skills are saved as markdown files with YAML frontmatter in `agent-data/skills/`:

```
Author a skill called "Competitive Analysis" with description "Analyze
competitors in a given market segment" and tags "analysis, research"
```

The agent calls `author_skill` and creates a `.md` file. You can also edit and read authored skills:

```
Edit the Competitive Analysis skill to include pricing comparison
Show me the Competitive Analysis skill
Delete the Competitive Analysis skill
```

## Skill Registry (Catalog)

The skill registry provides a searchable catalog of pre-built skill templates. Browse and install skills from the catalog:

```
Search available skills for "analysis"
Show me the skill catalog
Install the "financial-report" skill
```

Installed skills with MCP endpoints return connection instructions so you can wire them up.

## Connecting External MCP Servers

The Model Context Protocol (MCP) lets you connect external data sources to the agent at runtime. Each MCP server exposes its own set of tools that become available to the agent.

### Connecting a Server

```
Connect to MCP server "my-data-server" at https://mcp.example.com/sse using SSE transport
```

The agent calls `connect_mcp_server`, establishes a connection, discovers the server's tools, and registers them in the dynamic tool registry. The tools become available immediately with the `mcp:` prefix.

Supported transports:

- **`sse`** -- Server-Sent Events over HTTP (most common).
- **`http`** -- Standard HTTP transport.

### Listing Connections

```
List my MCP connections
```

Shows all active MCP server connections and the tools each provides.

### Disconnecting a Server

```
Disconnect the my-data-server MCP connection
```

Removes the connection and unregisters its tools.

## How Tools Are Registered

AI Agent Canvas uses a dynamic tool registry that aggregates tools from multiple sources:

1. **Sample tools** -- Compiled into the application (e.g., MarketData tools). Replace or extend with your own.
2. **Agent data tools** -- Persona, context, workflow, entity, profile, guardrail, and goal management tools.
3. **Scheduling and autonomous tools** -- Scheduled tasks, start/stop autonomous mode, manage the work queue.
4. **Inter-agent tools** -- Agent registry, mailbox, and handoff tools for multi-agent collaboration.
5. **Dynamic MCP tools** -- Registered at runtime when you connect an MCP server.

All tools are available to the agent simultaneously. The agent selects the appropriate tool based on your request.

## Inter-Agent Communication Tools

AI Agent Canvas supports multi-agent collaboration. Each persona becomes a named agent that can be messaged, delegated to, or handed off to.

| Tool | Description |
|------|-------------|
| `list_available_agents` | List all agents available for handoff or messaging (built from personas). |
| `get_agent_info` | Get details about a specific agent. |
| `handoff_to_agent` | Delegate a task to another agent synchronously and get the result back. |
| `send_to_agent` | Send an asynchronous message to another agent's mailbox. |
| `check_inbox` | Check for pending messages from other agents. |
| `reply_to_message` | Reply to a message from another agent. |

Example usage:

```
List available agents
Hand off to the financial-analyst agent: "Analyze AAPL earnings for Q4"
Send a message to the code-reviewer agent: "Please review the latest changes"
```

## Tips

- **Skills are LLM-powered** -- They run prompt templates, not code. This makes them easy to create but means they depend on the LLM's capabilities.
- **MCP connections persist during the session** but are not saved across backend restarts. Reconnect MCP servers after restarting.
- **Tool names must be unique.** If an MCP server provides a tool with the same name as a built-in tool, the MCP version is registered under the `mcp:server-name` namespace.

---
