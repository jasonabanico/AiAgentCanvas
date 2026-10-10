# 13. Running Fully Local

AI Agent Canvas can run with no outside service at all. The model, the embedding model, the vector store, the chat history and all other state live on your machine or on your network. This guide covers the `Local` provider, the models that call tools well enough to run an agent, the machine to run them on, and the settings that keep the host isolated.

## What fully local means here

| Piece | Where it runs |
|---|---|
| Chat model | A model server on this machine or this network: Ollama, llama.cpp's `llama-server`, LM Studio or vLLM |
| Embedding model | The same server, a second model |
| Vector store, chat history, memory, run ledger, audit log | SQLite files next to the host |
| Frontend | The static export the host serves, or `npm run dev` |

The `Local` provider speaks the OpenAI wire protocol, as the Databricks provider does. Any server that exposes `/v1/chat/completions` and `/v1/embeddings` works.

**What can still leave the machine.** The model is the only default dependency. These features reach outside when you turn them on, so leave them off for an isolated host:

| Feature | What it contacts |
|---|---|
| `DataConnections:MarketData` | Yahoo Finance and SEC EDGAR |
| `Databricks` and `Snowflake` vector search tools | The workspace or account you configure |
| `ComputerUse` | Whatever sites the agent browses |
| `Connectors` (Twilio SMS, Gmail, other MCP servers) | The connected service |
| `Mcp` with an HTTP server you name | That server |
| `ApplicationInsights:ConnectionString` | Azure Monitor |
| Purview | Microsoft Purview |
| `Vision` with `AllowedUrlHosts` | The hosts you list |

## Set it up

1. Install a model server. With Ollama:

   ```bash
   ollama pull gpt-oss:120b
   ollama pull nomic-embed-text
   ```

2. Point the host at it in `appsettings.json` (or `appsettings.Development.json`):

   ```json
   {
     "Provider": "Local",
     "Local": {
       "Endpoint": "http://localhost:11434/v1",
       "ModelName": "gpt-oss:120b",
       "EmbeddingModelName": "nomic-embed-text"
     },
     "Features": { "Rag": true, "EpisodicMemory": true }
   }
   ```

3. Start the host. The log line `Creating local chat client` names the endpoint and model, and the health endpoint at `/api/health` confirms the model answers.

| Server | Default endpoint |
|---|---|
| Ollama | `http://localhost:11434/v1` |
| LM Studio | `http://localhost:1234/v1` |
| llama.cpp `llama-server` | `http://localhost:8080/v1` |
| vLLM | `http://localhost:8000/v1` |

| Setting (`Local`) | Default | Meaning |
|---|---|---|
| `Endpoint` | `http://localhost:11434/v1` | Base URL, including `/v1` |
| `ApiKey` | `not-needed` | Most local servers ignore it. The client requires a value. |
| `ModelName` | none | The model tag the server knows. Required. |
| `EmbeddingModelName` | none | Turns on embeddings for RAG and episodic memory |
| `EconomyModelName` | none | A smaller model for the cost router and history summaries |
| `JudgeModelName` | none | A different model that grades the evaluation suite |
| `AllowNonLocalEndpoint` | false | See the next section |
| `RequestTimeoutSeconds` | 300 | Local models answer slowly on long prompts |

## The isolation guard

The provider refuses an endpoint outside the machine and the private networks. A typo that points an isolated host at a public address would otherwise send prompts, tool results and documents across the internet. These count as local: loopback, the private ranges `10.0.0.0/8`, `172.16.0.0/12` and `192.168.0.0/16`, link-local addresses, the carrier-grade NAT range `100.64.0.0/10` that Tailscale uses, IPv6 unique-local addresses, single-label host names such as `mac-studio`, and the suffixes `.local`, `.lan`, `.internal` and `.home.arpa`.

A model server on another machine in your office is allowed. A hosted API is refused with a message that names the setting. Set `Local:AllowNonLocalEndpoint` to true if you mean to use one.

## Choosing a model

The host runs the model in a tool loop, so the model has to call tools reliably over many rounds. A small model that answers questions well can still fail there. The figures below are the ones the model makers and roundup articles report, so test any candidate on your own tasks before you buy hardware.

| Memory available | Model | Notes |
|---|---|---|
| 128 GB | gpt-oss-120b | Native function calling and structured output. The weights take about 65 GB, which leaves room for a long context. Qwen3.5-122B-A10B and Nemotron 3 Super 120B-A12B are the alternatives at this size. |
| 64 GB | Qwen3-Coder-Next | 80B total and 3B active parameters, 262K context, Apache 2.0, native tool calling |
| 24 to 32 GB | Qwen3.6-35B-A3B, or GLM-4.7-Flash | A small active parameter count gives usable speed |
| 16 GB | gpt-oss-20b | The smallest model worth trusting with a multi-step tool loop |

Run two small models beside the main one: an embedding model such as `nomic-embed-text` or `bge-m3`, and a 9B model for `EconomyModelName`.

Mixture-of-experts models suit machines with large, slow memory. Only a few billion parameters are active for each token, so a 120B model decodes at a usable rate on a unified-memory box.

## The machine

| Part | Specification |
|---|---|
| Memory | 128 GB unified, or 48 GB or more of GPU memory. The weights, a 64K to 128K context cache, the embedding model and the operating system all have to fit. |
| Memory bandwidth | 250 GB/s or more. Decoding speed rises with bandwidth. |
| CPU | 16 cores |
| Storage | 2 TB NVMe. Each model is 20 to 70 GB. |
| Power | About 120 to 140 W under load |

The smallest useful machine has a 32 GB GPU and 64 GB of system memory, which runs the 30B-class models.

Checked on 11 October 2026 at amazon.com. Prices vary with the delivery location, stock changes quickly, and the memory shortage has pushed GPU prices well above list.

| Machine | Memory and bandwidth | Price seen |
|---|---|---|
| [GMKtec EVO-X2](https://www.amazon.com/GMKtec-ryzen_ai_mini_pc_evo_x2/dp/B0F53MLYQ6), Ryzen AI Max+ 395, 2 TB | 128 GB, about 256 GB/s | $3,649.99, in stock |
| [ASUS Ascent GX10](https://www.amazon.com/Supercomputer-Superchip-Supports-OpenClaw-Stackable/dp/B0H9YVH6LL), NVIDIA GB10, 1 TB | 128 GB, 273 GB/s | $8,990 from a third party, unavailable |
| [NVIDIA DGX Spark](https://www.amazon.com/NVIDIA-DGX-SparkTM-Supercomputer-Blackwell/dp/B0FWJ16CCH) | 128 GB, 273 GB/s | Unavailable on Amazon. Buying guides give $4,699 to $6,950. |
| [Mac Studio M4 Max](https://www.amazon.com/Apple-Studio-16-Core-40-Core-Unified/dp/B0FMFZ5SXS), 128 GB | 128 GB, 546 GB/s | Unavailable on Amazon |
| [Mac Studio M5 Max](https://www.amazon.com/Apple-2026-Studio-Desktop-Computer/dp/B0HGKSQMX6) | Up to 128 GB, 460 to 614 GB/s | Amazon lists only the 36 GB model. A buying guide gives about $5,100 for 128 GB. |
| [PNY RTX 5090](https://www.amazon.com/PNY-GeForce-Overclocked-Graphics-3-5-Slot/dp/B0DTJF8YT4), GPU only | 32 GB, 1,792 GB/s | $6,199 against a $1,999 list price, plus a PC around it |

The Ryzen AI Max+ 395 box is the lowest-cost route to 128 GB. A Mac Studio decodes faster. A GB10 machine is the choice when you need CUDA. The RTX 5090 is the fastest card, but its 32 GB limits you to the 30B-class models.

## Tuning for a local model

- **Set the context window.** Servers load a model with a context length of their own, and some default to a few thousand tokens. Set it on the server (for Ollama, the `OLLAMA_CONTEXT_LENGTH` variable or `num_ctx` in a Modelfile) and set `Agent:ContextBudget:MaxContextTokens` to the same number. The budget counts tokens against that figure.
- **Keep the tool list short.** A small model picks the wrong tool more often as the list grows. Give each agent only the tools it needs with an `IAgentToolsSeed`, or turn on `Agent:ToolSelection` when the host registers many.
- **Lower the round cap.** `Agent:LoopGuard:MaxToolRounds` defaults to 25. A local model that is going to fail usually fails sooner, so 10 to 15 returns control faster.
- **Price the model at zero.** An unpriced model reports tokens and no cost. Add the model to `Agent:Pricing:Models` with `InputPer1M` and `OutputPer1M` set to 0 if you want the run ledger and the budgets to treat the cost as nothing.
- **Use the economy model.** `Local:EconomyModelName` takes the history summaries off the large model, and the simple turns too when `Agent:ModelRouter` is on.
- **Plan for slow prompts.** Prompt processing on unified-memory machines is slow for the first token of a long prompt. A shorter system prompt, fewer enabled features and a smaller `Agent:Rag:TopK` all help.
- **Keep one embedding model.** Vectors from different embedding models cannot be compared. Changing `EmbeddingModelName` means deleting the indexed documents and indexing them again. The `1536` in the vector attribute is not enforced, so models with 768 or 1024 dimensions work as long as one model produced every vector.

## Check it

1. `curl http://localhost:5149/api/health` returns `Healthy` and shows the pipeline check passed.
2. Send a message in the chat. The model server's own log shows the request.
3. With `Rag` on, index a document with `POST /api/rag/documents` and ask about it. The server log shows an embeddings request and then a chat request.
4. To prove nothing leaves the host, run it with outbound traffic blocked at the firewall. A chat, RAG and memory still work.

## Limits

- A local model calls tools less reliably than a large hosted one. Test the tasks you care about, and keep unattended work on the `Review` orchestration kind or behind approvals until you trust the results.
- The MCP client supports HTTP servers. It does not start a local process over stdio, so a stdio-only MCP server needs an HTTP wrapper.
- A local server handles a few requests at a time, a setting on most of them. Concurrent orchestration runs queue behind each other.
- Prompt caching, a hosted-API feature, is not used.
