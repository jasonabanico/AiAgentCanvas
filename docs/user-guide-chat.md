> [User Guide](user-guide.md) > Chat Interface

# User Guide: Chat Interface

AI Agent Canvas uses [CopilotKit](https://copilotkit.ai/) to provide a real-time chat interface where you interact with the agent. This page explains how the chat UI works and what to expect.

## Overview

The chat interface is a single-page application built with Next.js and the CopilotKit React components. It connects to the backend via the AG-UI protocol (Server-Sent Events) at the `/api/copilotkit` endpoint.

When you open **http://localhost:3000** in your browser, you see a full-screen chat panel ready for input.

## Sending Messages

Type your message in the input field at the bottom of the chat panel and press **Enter** to send. The agent receives your message, processes it, and streams back a response.

You can send:

- **Questions** -- "What is the current price of AAPL?"
- **Commands** -- "Create a persona called Analyst."
- **Multi-step requests** -- "Get stock quotes for AAPL, MSFT, and GOOGL, then summarize the trends."

## Streaming Responses

Responses stream in token by token as the LLM generates them. You see text appearing in real time rather than waiting for the full response to complete. This gives immediate feedback that the agent is working.

Long responses may take several seconds to complete. The streaming indicator shows that generation is still in progress.

## Tool Calls

When the agent needs to perform an action -- such as fetching a stock quote, creating a persona, or scheduling a task -- it makes a **tool call**. Tool calls appear in the chat as the agent transparently shows what action it is taking.

A typical tool call flow:

1. You send a message: "Get a stock quote for MSFT."
2. The agent decides to call the `stock_quote` tool.
3. The tool executes and returns data.
4. The agent incorporates the result into its response.

The agent has access to 70+ built-in tools spanning personas, context, workflows, entities, user profiles, guardrails, goals, skills, scheduling, and MCP connections. See [Skills & Tools](user-guide-skills.md) for details.

## Conversation Context

The agent maintains context throughout a conversation. It remembers what you discussed earlier in the session and can reference previous messages, tool results, and decisions.

Several systems contribute to the agent's context:

| Context Source | What It Provides |
|---------------|-----------------|
| Conversation history | All messages and tool results from the current session |
| Active persona | Custom instructions from the currently selected persona |
| Active user profile | Your name, role, timezone, and preferences |
| Persistent context | Saved context entries that carry across sessions |
| Entity memory | Known entities (people, companies, projects) |
| Guardrail rules | Active safety rules that shape behavior |

These context sources are injected into the agent's system prompt automatically. You do not need to repeat information that is already stored.

## Notifications

The backend can push real-time notifications to the frontend via Server-Sent Events (SSE) at `/api/notifications`. Scheduled tasks and background processes use this channel to report results without interrupting your current conversation.

## Tips for Effective Conversations

- **Be specific** -- "Get the stock quote for AAPL" works better than "tell me about Apple."
- **Chain requests** -- The agent can handle multi-step instructions in a single message.
- **Reference previous context** -- "Now do the same for MSFT" works when the agent has context from an earlier request.
- **Use natural language for management tasks** -- "List my personas," "Show scheduled tasks," and "What workflows do I have?" all work as expected.
- **Save important information** -- Ask the agent to save entities or context entries for facts you want it to remember across sessions.

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Chat loads but messages fail | Check that the backend is running on port 5000. Verify `NEXT_PUBLIC_BACKEND_URL` in `frontend/.env.local`. |
| No response appears | Check the backend terminal for errors. Verify your `AIFoundry` settings in `appsettings.json`. |
| Responses are slow | Large models take longer. Consider using a faster deployment (e.g., `gpt-4o-mini`). Check your rate limit settings. |
| Tool calls fail | Check the backend logs for the specific tool error. Ensure network connectivity to Yahoo Finance and SEC EDGAR. |
| Connection refused | Make sure both the backend (`dotnet run`) and frontend (`npm run dev`) are running. |

## Architecture Note

The frontend is intentionally thin. It renders the CopilotKit chat component and points it at the backend's AG-UI endpoint. All agent logic, tool execution, and context management happen server-side. This means you can swap or customize the frontend without changing any agent behavior.

---
