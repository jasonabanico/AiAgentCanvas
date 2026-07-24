# 2. What Is an AI Agent

An AI agent is software that uses an LLM to reason about a goal, decide what actions to take, execute them through tools, and iterate until the goal is met. Unlike a chatbot that generates text in response to prompts, an agent acts on your behalf -- it reads data, calls APIs, modifies systems, and makes decisions across multiple steps.

## The Agent Loop

Every agent interaction follows the same cycle:

```
User message
    -> Context injection (persona, memory, guardrails, domain knowledge)
    -> LLM reasoning (understand the goal, plan next steps)
    -> Tool calls (execute actions via functions, APIs, or external systems)
    -> Tool results (observe outcomes)
    -> LLM reasoning (evaluate results, decide if done or continue)
    -> Response (deliver the final answer or ask for clarification)
```

This loop repeats as many times as needed. A simple question might take one pass. A complex task -- researching a topic, pulling data from three systems, formatting a report -- might take ten. The agent decides when it has enough information to respond.

In code, this loop is managed by the agent runtime. Here is a simplified version of what happens on each turn:

```csharp
// The runtime assembles context and runs the loop
var systemPrompt = contextManager.BuildSystemPrompt(agent, user, conversation);
var messages = new List<ChatMessage> { new SystemChatMessage(systemPrompt) };
messages.AddRange(conversation.History);
messages.Add(new UserChatMessage(userInput));

// The FunctionInvokingChatClient handles the tool-call loop automatically
var response = await chatClient.GetResponseAsync(messages, options);
```

The `FunctionInvokingChatClient` from Microsoft.Extensions.AI intercepts tool-call responses from the LLM, executes the corresponding functions, feeds results back, and lets the LLM continue reasoning -- all within a single call.

## What Makes Agents Different from LLM Wrappers

A thin LLM wrapper sends a prompt and returns the response. An agent goes further:

- **Tool use** -- agents call functions to interact with external systems. They don't just describe what should happen; they do it. A wrapper tells you the API call to make. An agent makes the call.
- **Context awareness** -- agents maintain state across turns: who the user is, what entities are in play, what happened earlier in the conversation, and what domain knowledge applies. Each turn builds on the last.
- **Multi-step reasoning** -- agents break complex goals into steps, execute them in sequence, and adapt based on intermediate results. If step 3 fails, the agent reasons about why and tries a different approach.
- **Persistence** -- agents remember across sessions. Entity memory, user preferences, and conversation history survive restarts. The agent picks up where it left off.

## Use Cases

| Domain | What the Agent Does |
|---|---|
| **Financial services** | Monitors portfolio risk, flags compliance violations, and generates regulatory reports from transaction data. |
| **IT operations** | Triages incidents from monitoring alerts, correlates logs across systems, and executes runbook steps to resolve issues. |
| **Customer support** | Resolves tickets by looking up account data, applying business rules, and taking actions like issuing refunds or updating records. |
| **Legal and compliance** | Reviews contracts against policy checklists, extracts key clauses, and flags deviations for human review. |
| **Healthcare** | Summarizes patient records, cross-references symptoms with clinical guidelines, and prepares pre-visit briefs for clinicians. |
| **Software engineering** | Reviews pull requests, generates test cases from specifications, and investigates build failures by reading logs and code. |
| **E-commerce** | Manages product catalog updates, processes return requests end-to-end, and generates pricing recommendations from sales data. |
