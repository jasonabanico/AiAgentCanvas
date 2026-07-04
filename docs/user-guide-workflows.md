> [User Guide](user-guide.md) > Workflows

# User Guide: Workflows

Workflows let you define multi-step agent sequences that run as a single operation. Instead of typing each step manually, you create a workflow once and run it whenever you need it.

## What Is a Workflow?

A workflow is a named sequence of steps stored as a markdown file. Each workflow has:

- **Name** -- A short identifier (e.g., "Morning Briefing").
- **Description** -- What the workflow accomplishes.
- **Tags** -- Optional labels for organization.
- **Content** -- The full workflow definition, including steps.

When you run a workflow, the agent reads its definition, builds a prompt from the steps, and executes it through the LLM. The agent has access to all its tools during execution, so workflow steps can fetch data, create entities, and perform any action the agent normally can.

## Creating a Workflow

Ask the agent to create a workflow in natural language:

```
Create a workflow called "Morning Briefing" with description "Daily market
overview" that does the following:
1. Get stock quotes for AAPL, MSFT, GOOGL, and AMZN
2. Get technical indicators for each stock
3. Summarize the overall market sentiment
4. List any stocks with significant price changes
```

The agent calls `create_workflow` and saves the workflow as a markdown file in `agent-data/workflows/`.

## Listing Workflows

To see all available workflows:

```
List my workflows
```

The agent calls `list_workflows` and returns the name, description, and tags of each workflow.

## Reading a Workflow

To view the full definition of a workflow:

```
Show me the Morning Briefing workflow
```

The agent calls `read_workflow` and displays the complete workflow content, including all steps.

## Running a Workflow

To execute a workflow:

```
Run the Morning Briefing workflow
```

The agent calls `run_workflow`, which:

1. Loads the workflow definition from its markdown file.
2. Builds a prompt from the workflow content.
3. Executes the prompt through the LLM with full tool access.
4. Returns the combined results.

The agent executes the steps sequentially, using tool calls as needed. You see the results stream in as the workflow progresses.

## Deleting a Workflow

```
Delete the Morning Briefing workflow
```

The agent calls `delete_workflow` and removes the markdown file.

## Workflow File Format

Workflows are stored as markdown files in `agent-data/workflows/`. Each file contains YAML frontmatter followed by the workflow content:

```yaml
---
name: Morning Briefing
description: Daily market overview
tags:
  - market
  - daily
---

## Steps

1. Get current stock quotes for AAPL, MSFT, GOOGL, and AMZN
2. Retrieve technical indicators for each stock
3. Summarize the overall market sentiment based on the data
4. Highlight any stocks with price changes greater than 2%
```

You can also edit these files directly if you prefer working with files over chat commands.

## Example Workflows

### Portfolio Review

```
Create a workflow called "Portfolio Review" that:
1. Gets stock quotes for my portfolio: AAPL, MSFT, GOOGL, AMZN, TSLA
2. Gets EDGAR company facts for any stock that moved more than 3%
3. Produces a summary table with ticker, price, change, and recommendation
```

### Competitor Analysis

```
Create a workflow called "Competitor Analysis" that:
1. Gets stock quotes for CRM, NOW, WDAY, and HUBS
2. Gets technical indicators for each
3. Compares their relative strength
4. Produces a competitive landscape summary
```

### End of Day Report

```
Create a workflow called "End of Day Report" that:
1. Gets stock quotes for the top 5 holdings
2. Summarizes the day's market movements
3. Lists any significant news or filings from EDGAR
4. Recommends actions for the next trading day
```

## Combining Workflows with Scheduling

Workflows become more powerful when combined with scheduled tasks. For example:

```
Schedule a recurring task every weekday at 9 AM to run the Morning Briefing workflow
```

This creates a Hangfire job that executes your workflow automatically. See [Scheduling](user-guide-scheduling.md) for details.

## Tips

- **Workflows are LLM-driven** -- Steps are instructions, not code. The LLM interprets and executes them using available tools.
- **Tool access is automatic** -- The agent can call any registered tool during workflow execution, including MCP connections.
- **Results depend on available data** -- If a tool call fails (e.g., API rate limit), the workflow continues with what it has and notes the failure.
- **Keep steps clear and specific** -- Vague steps produce inconsistent results. "Get the stock quote for AAPL" works better than "check the market."
- **Edit files directly** -- Workflow markdown files in `agent-data/workflows/` can be edited with any text editor for fine-tuning.

---
