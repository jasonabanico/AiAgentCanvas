# Typed Output, Vision, Orchestration and MCP Server

This guide covers four capabilities that widen what an agent can do and what can call it. Typed output makes a model answer in a shape a program can rely on. Vision lets a model look at images. Orchestration runs several agents together, with durable state and a human decision point. The MCP server lets outside clients call chosen tools.

Each is off by default behind a `Features:*` flag.

| Flag | Adds | Needs |
|------|------|-------|
| `StructuredOutput` | Answers checked against a JSON Schema and retried until they fit | none |
| `Vision` | Tools that describe an image or extract typed data from it | A vision-capable model. `StructuredOutput` adds the extraction tool. |
| `AgentOrchestration` | Group chat, handoff and Magentic runs that survive a restart | `InterAgentCommunication` |
| `McpServer` | A Model Context Protocol endpoint serving a chosen set of tools | Authentication on, or an explicit opt-out |

## Typed output

A model that is asked for JSON often returns JSON with a missing field, a string where a number belongs, or a sentence wrapped around the object. The structured responder expects that and does not trust the first answer.

For each request it does the following:

1. Sends the schema twice: as the provider's native response format, and in the instruction text. A provider that ignores the format still follows the text.
2. Parses the answer. A code fence or a sentence around the JSON is tolerated.
3. Checks the parsed value against the schema.
4. On a failure, sends the answer back with the specific errors (`$.age: expected integer but found string`) and asks again, up to the attempt limit.

The result says whether it succeeded, the value, the errors from the last attempt, how many attempts it took, and the raw text of the last answer. A failure comes back as a result and does not throw, so a caller does not mistake a default value for an answer.

The responder runs on the same chat pipeline as an agent, so each attempt is counted, priced and limited like any other model call.

**From code.** The shape can live in a class. `RespondAsync<T>` builds the schema from the type and returns a typed value.

```csharp
public sealed record Weather(string City, double TempC);

var result = await responder.RespondAsync<Weather>(
    "Extract the weather.", "It is 21.5 degrees in Sydney.");

if (result.Success) Console.WriteLine(result.Value!.City);
```

**From an agent.** The tool `extract_structured` takes the text, an instruction, and either `schemaName` (a stored schema) or `schema` (a JSON Schema document as text). `list_schemas` shows the stored ones. The text may come from an outside party, so it can fill the fields but cannot change the shape or the instruction.

**Stored schemas.** Files named `*.schema.json` in `Agent:Structured:SchemaDirectory` are loaded at start. The file name without the suffix is the schema name. A file that is not valid JSON is skipped with a warning.

**From HTTP.** `POST /api/structured/extract` takes `instruction`, `input`, `schemaName` or `schema`, and `maxAttempts`. `GET /api/structured/schemas` lists the names. The endpoint key is `structured`.

**What the validator checks.** `type` (including a list of types), `properties`, `required`, `additionalProperties`, `items`, `enum`, `const`, `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum`, `minLength`, `maxLength`, `pattern`, `minItems`, `maxItems`, `anyOf`, `oneOf`, `allOf`, and local `$ref` into `$defs` or `definitions`. It ignores other keywords, such as `format`, and does not treat them as failures. Errors are capped at 20 so a long bad array does not flood the retry message.

| Setting (`Agent:Structured`) | Default | Meaning |
|---|---|---|
| `SchemaDirectory` | `schemas` | Folder of `*.schema.json` files |
| `MaxAttemptsCap` | 5 | The most attempts any request may use |
| `MaxInputChars` | 100000 | Longest input accepted |
| `MaxSchemaChars` | 20000 | Longest schema accepted from a caller |

## Vision

The vision tools pass images to a model that can read them. They need a vision-capable model behind the pipeline. A text-only model rejects the request, and the tool reports the model's error.

| Tool | Does |
|---|---|
| `describe_image` | Answers a question about one or more images, or describes them |
| `extract_from_image` | Reads an image into a JSON shape, checked and retried like typed output. Present only when `StructuredOutput` is on. |

Both take `sources`: file paths or https addresses. Text in an image comes from whoever made it. The tools mark their result as untrusted content, and the model should treat it as data.

**Where images may come from.** The same rule the system tools follow applies: an empty list denies everything.

- `AllowedPaths` lists the folders files may be read from. A path outside them is refused, including a `..` path, a sibling folder whose name starts with an allowed one, and a link inside an allowed folder that points out of it.
- `AllowedUrlHosts` lists the hosts an image may be fetched from: an exact name or `*.example.com`. Only https is fetched. An address with a user name or password is refused.
- The fetch follows no redirects. Before connecting, it refuses private, loopback and link-local addresses unless `AllowPrivateNetworks` is true. The check runs on the address actually connected to, so a name that resolves to an internal address does not get through.

**What is accepted.** PNG, JPEG, GIF and WebP, decided by the file's first bytes and not by its name or the server's claim. SVG is refused because it can carry script. The size limit is enforced while reading, so a server that lies about its length is still stopped.

| Setting (`Agent:Vision`) | Default |
|---|---|
| `AllowedPaths` | empty (no files) |
| `AllowedUrlHosts` | empty (no fetching) |
| `AllowPrivateNetworks` | false |
| `MaxImageBytes` | 5242880 |
| `MaxImagesPerCall` | 4 |
| `FetchTimeoutSeconds` | 15 |

Images reach an agent through these tools. Attaching an image in the chat window is not supported.

## Orchestration

An orchestration run puts several named agents on one task. Three kinds ship.

| Kind | How it works |
|---|---|
| `GroupChat` | The agents take turns on one shared conversation, round robin, until the round limit |
| `Handoff` | A lead agent passes the conversation to a specialist by calling a handoff tool, and the specialist can pass it back. The model must support tool calls. |
| `Magentic` | A manager agent writes a plan, assigns work to the team, tracks progress and replans when the team stalls |

The agents come from the agent registry, so each is a persona the host already knows. Each runs on the standard pipeline, which means a limit, a cost counter or a governance rule applies to a run as it does to a chat.

**Durable by design.** Each step is checkpointed to disk, and the run record lives in SQLite. A Magentic run asks for a person to approve its plan (`requireSignoff`, on by default). It then stops completely: nothing is running and nothing is waiting in memory. The record holds the plan and a notification goes out. When the person answers, possibly days later and after a restart, the host rebuilds the workflow from the record, restores the checkpoint and continues from the same point.

| Status | Meaning |
|---|---|
| `Running` | Working now |
| `WaitingForInput` | Stopped at a checkpoint for a person's decision |
| `Completed` | Finished, with a result and a transcript |
| `Failed` | An agent or the model raised an error, or the run exceeded `RunTimeoutMinutes` |
| `Cancelled` | A person or an agent cancelled it |
| `Interrupted` | The process stopped mid-run. Resume it from the last checkpoint. |

**A person answers, an agent does not.** The tools `start_orchestration`, `get_orchestration`, `list_orchestrations` and `cancel_orchestration` are available to agents. There is no tool to approve or revise a plan. An agent that could approve its own plan would make the sign-off meaningless. The answer goes through `POST /api/orchestrations/{id}/respond` (endpoint key `orchestrations`) or the **Orchestrations** tab, which shows the plan with **Approve**, **Ask for changes** (with feedback) and **Cancel run**.

Other endpoints: `GET /api/orchestrations` (filter with `status`), `GET /api/orchestrations/{id}`, `POST /api/orchestrations` to start, `POST /api/orchestrations/{id}/resume` for an interrupted run, and `POST /api/orchestrations/{id}/cancel`.

**Recording.** Each run appears in the run ledger with source `Orchestration`, so its cost and outcome sit with every other run. After a restart, runs left in `Running` are marked `Interrupted` and the log says how many. Finished runs and their checkpoints are deleted after `RetentionDays`. A run waiting for a person is kept however old it is.

| Setting (`Agent:Orchestration`) | Default |
|---|---|
| `DatabasePath` | `orchestrations.db` |
| `CheckpointDirectory` | `orchestration-checkpoints` |
| `DefaultMaxRounds` | 8 |
| `MaxRoundsCap` | 30 |
| `MaxAgents` | 8 |
| `RunTimeoutMinutes` | 15 |
| `RetentionDays` | 30 |

**Limits.**

- The only request a run can stop for is a Magentic plan review. A run built from code with a custom request port is not covered.
- The group chat manager is round robin. A manager that picks the next speaker with a model is not offered.
- A run executes inside one process. Checkpoints make a restart safe, and two hosts do not share a run.
- A handoff depends on the model calling the handoff tool. A model that ignores tools finishes with the lead.

## MCP server

The MCP server lets another agent, an IDE or a workflow engine call tools on this host through the Model Context Protocol. It is the reverse of the MCP connector, which lets this host call someone else's tools.

Nothing is exposed until `Agent:McpServer:ExposedTools` names it. A name matches exactly, or by family with a trailing `*` (`rag_*`).

```json
{
  "Features": { "McpServer": true },
  "Agent": {
    "McpServer": {
      "ExposedTools": ["extract_structured", "list_schemas", "rag_*"]
    }
  }
}
```

**Safety rules.**

- **Authentication.** The endpoint (default `/mcp`, endpoint key `mcp`) hands tools to whoever can reach it. The host refuses to start with `McpServer` on and `Authentication:Enabled` false. Set `Agent:McpServer:AllowUnauthenticated` to true only when the endpoint is reachable from a trusted network alone.
- **Approval.** A tool that needs a person's approval is not offered, even when named, because an MCP call has no one to ask. The log says so.
- **Governance and tracing.** Each exposed tool passes through the same governance wrapper and tracing as when an agent calls it. A governance rule that blocks a tool for an agent blocks it here.
- **Audit.** Each outside call is recorded in the run ledger as a run with source `External`, with the tool name, its arguments (cut to 500 characters) and a preview of the result.
- **Result size.** A result longer than `MaxResultChars` is cut before it leaves, and the cut is marked.
- **Missing names.** A configured name the host does not register is logged and skipped. The rest still load.
- **Connector tools.** Tools that come from connections are not exposed, because they appear and disappear as connections change. Only tools registered at start are offered.

| Setting (`Agent:McpServer`) | Default |
|---|---|
| `Path` | `/mcp` |
| `ServerName` | `AiAgentCanvas` |
| `ExposedTools` | empty (nothing) |
| `AllowUnauthenticated` | false |
| `MaxResultChars` | 100000 |
