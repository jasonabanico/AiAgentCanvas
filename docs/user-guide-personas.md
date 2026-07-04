> [User Guide](user-guide.md) > Personas

# User Guide: Personas

Personas let you switch the agent's personality, tone, and focus area without changing any code. Each persona is a set of custom instructions that shape how the agent responds.

## What Is a Persona?

A persona is a named configuration with:

- **Name** -- A short identifier (e.g., "Analyst", "Advisor").
- **Description** -- What the persona is for.
- **Instructions** -- Detailed instructions injected into the agent's system prompt.

When a persona is active, its instructions are prepended to every agent response, guiding the agent's behavior, tone, and areas of focus. When no persona is active, the agent uses its default system prompt.

## Creating a Persona

Ask the agent to create one in natural language:

```
Create a persona called "Financial Analyst" with instructions to focus on
quantitative analysis, cite data sources, use precise financial terminology,
and present findings in a structured format with bullet points.
```

The agent calls the `create_persona` tool and saves the persona as a markdown file in `agent-data/personas/`.

## Listing Personas

To see all available personas:

```
List my personas
```

The agent calls `list_personas` and returns the name and description of each persona.

## Switching Personas

To activate a different persona:

```
Switch to the Financial Analyst persona
```

The agent calls `switch_persona`, and all subsequent responses in the session use that persona's instructions. The active persona is tracked in an `.active` file, so it persists across backend restarts.

## Reading a Persona

To view the full details of a persona:

```
Show me the Financial Analyst persona
```

The agent calls `read_persona` and displays the persona's name, description, and instructions.

## Updating a Persona

To change a persona's instructions:

```
Update the Financial Analyst persona to also include risk assessment in every analysis
```

The agent calls `update_persona` and saves the changes.

## Deleting a Persona

To remove a persona:

```
Delete the Financial Analyst persona
```

The agent calls `delete_persona` and removes the markdown file.

## How Personas Work Internally

Personas are managed by three components:

1. **PersonaStore** -- Reads and writes persona markdown files in `agent-data/personas/`. Each persona is a separate `.md` file.
2. **PersonaToolProvider** -- Exposes the six persona tools (`create_persona`, `update_persona`, `list_personas`, `switch_persona`, `read_persona`, `delete_persona`) to the agent.
3. **PersonaContextProvider** -- An AI context provider that reads the active persona's instructions and injects them into the agent's system prompt before every interaction.

## Example Personas

### Data Analyst

```
Create a persona called "Data Analyst" with instructions:
- Focus on quantitative analysis and data interpretation
- Always ask for data sources and sample sizes
- Present findings with tables and bullet points
- Flag statistical anomalies and outliers
```

### Executive Advisor

```
Create a persona called "Executive Advisor" with instructions:
- Provide concise, strategic recommendations
- Lead with the bottom line, then support with details
- Frame insights in terms of business impact and ROI
- Use professional but accessible language
```

### Research Assistant

```
Create a persona called "Research Assistant" with instructions:
- Be thorough and cite sources whenever possible
- Present multiple perspectives on complex topics
- Organize findings with clear headings and sections
- Flag areas where information is uncertain or incomplete
```

## Tips

- **Only one persona is active at a time.** Switching personas replaces the previous one.
- **Persona instructions stack with other context.** Guardrails, user profiles, entities, and persistent context are still active alongside the persona.
- **Clear the persona** by asking "Switch to the default persona" or "Deactivate the current persona."
- **Keep instructions focused.** Shorter, clearer instructions produce more consistent behavior than long, complex ones.

---
