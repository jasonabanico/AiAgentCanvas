#pragma warning disable MEAI001

using AiAgentCanvas.Abstractions;
using AiAgentCanvas.AgentData.Context;
using AiAgentCanvas.AgentData.Entities;
using AiAgentCanvas.AgentData.Guardrails;
using AiAgentCanvas.AgentData.Personas;
using AiAgentCanvas.AgentData.Profiles;
using AiAgentCanvas.AgentData.Workflows;
using AiAgentCanvas.Capabilities.Notifications;
using AiAgentCanvas.Capabilities.Rag;
using AiAgentCanvas.Capabilities.Scheduling;
using AiAgentCanvas.Capabilities.Skills;
using AiAgentCanvas.Capabilities.AuditLog;
using AiAgentCanvas.Capabilities.EpisodicMemory;
using AiAgentCanvas.Capabilities.ComputerUse;
using AiAgentCanvas.Capabilities.EventTriggers;
using AiAgentCanvas.Capabilities.SystemTools;
using AiAgentCanvas.Host;
using AiAgentCanvas.Orchestration;
using AiAgentCanvas.Storage.Sqlite;
using AiAgentCanvas.Providers.AzureAIFoundry;
using AiAgentCanvas.Providers.Databricks;
using AiAgentCanvas.Providers.Snowflake;
using AiAgentCanvas.Security;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using DataConnection.VectorStore.Sqlite;
using DataConnection.VectorSearch.Databricks;
using DataConnection.VectorSearch.Snowflake;
using Microsoft.Agents.AI.DevUI;

var builder = WebApplication.CreateBuilder(args);

var features = new FeatureFlags();
builder.Configuration.GetSection(FeatureFlags.SectionName).Bind(features);

if (!string.IsNullOrEmpty(builder.Configuration["ApplicationInsights:ConnectionString"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o =>
        o.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"]);
}

var llmProvider = builder.Configuration["Provider"] ?? "AzureAIFoundry";
if (string.Equals(llmProvider, "Databricks", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDatabricks(builder.Configuration);
}
else if (string.Equals(llmProvider, "Snowflake", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSnowflake(builder.Configuration);
}
else
{
    builder.Services.AddAzureAIFoundry(builder.Configuration);
}

builder.Services.AddAiAgentCanvasSecurity(builder.Configuration);
builder.Services.AddAiAgentCanvasPurview(builder.Configuration);
builder.Services.AddDevUI();

builder.Services.AddAiAgentCanvas(builder.Configuration, options =>
{
    options.AgentName = "AiAgentCanvas";
    options.AgentDescription = "A multi-tool AI assistant with market data, scheduling, skills, and MCP integration.";
});

builder.Services.AddServiceModules(builder.Configuration);

var databricksVectorSearch = new DatabricksVectorSearchOptions();
builder.Configuration.GetSection(DatabricksVectorSearchOptions.SectionName).Bind(databricksVectorSearch);
if (databricksVectorSearch.IsConfigured)
{
    builder.Services.AddDatabricksVectorSearchTool(builder.Configuration);
}

var snowflakeCortexSearch = new SnowflakeCortexSearchOptions();
builder.Configuration.GetSection(SnowflakeCortexSearchOptions.SectionName).Bind(snowflakeCortexSearch);
if (snowflakeCortexSearch.IsConfigured)
{
    builder.Services.AddSnowflakeCortexSearchTool(builder.Configuration);
}

if (features.SystemTools)
{
    builder.Services.AddAiAgentCanvasSystemTools(options =>
    {
        options.AllowedCommands = ["dotnet", "git", "npm", "node"];
        options.ScriptTimeoutSeconds = 30;
    });
}

if (features.Notifications) builder.Services.AddAiAgentCanvasNotifications();

if (features.Scheduling)
{
    builder.Services.AddSqliteScheduledTaskStore();
    builder.Services.AddAiAgentCanvasScheduler();
}

if (features.Skills) builder.Services.AddAiAgentCanvasSkills();
if (features.Mcp) builder.Services.AddAiAgentCanvasMcp();
if (features.Personas) builder.Services.AddAiAgentCanvasPersonas();
if (features.Context) builder.Services.AddAiAgentCanvasContext();
if (features.Workflows) builder.Services.AddAiAgentCanvasWorkflows();
if (features.Entities) builder.Services.AddAiAgentCanvasEntities();
if (features.UserProfiles) builder.Services.AddAiAgentCanvasUserProfiles();
if (features.Guardrails) builder.Services.AddAiAgentCanvasGuardrails();
if (features.SkillRegistry) builder.Services.AddAiAgentCanvasSkillRegistry();
if (features.SkillAuthoring) builder.Services.AddAiAgentCanvasSkillAuthoring();
if (features.EpisodicMemory) builder.Services.AddAiAgentCanvasEpisodicMemory();
if (features.AuditLog) builder.Services.AddAiAgentCanvasAuditLog();
if (features.EventTriggers) builder.Services.AddAiAgentCanvasEventTriggers();
if (features.ComputerUse) builder.Services.AddAiAgentCanvasComputerUse();

if (features.InterAgentCommunication)
{
    builder.Services.AddAiAgentCanvasInterAgentCommunication(
        personaLookupFactory: sp =>
        {
            var store = sp.GetRequiredService<PersonaStore>();
            return name =>
            {
                var p = store.GetPersona(name);
                return p is null ? null : new AgentPersonaInfo
                {
                    Name = p.Name,
                    Description = p.Description,
                    Instructions = p.Instructions,
                };
            };
        },
        personaListAllFactory: sp =>
        {
            var store = sp.GetRequiredService<PersonaStore>();
            return () => store.ListPersonas().Select(p => new AgentPersonaInfo
            {
                Name = p.Name,
                Description = p.Description,
                Instructions = p.Instructions,
            });
        },
        agentName: "AiAgentCanvas");
}

builder.Services.AddSqliteChatHistory();

if (features.Rag)
{
    // Snowflake Cortex embeddings are not confirmed OpenAI-/embeddings-compatible, so RAG
    // embeddings are wired only for Databricks and Azure AI Foundry (explicit provider match).
    var databricksEmbeddings = string.Equals(llmProvider, "Databricks", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(builder.Configuration["Databricks:EmbeddingModelName"]);
    var azureEmbeddings = string.Equals(llmProvider, "AzureAIFoundry", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(builder.Configuration["AIFoundry:EmbeddingDeploymentName"]);

    if (databricksEmbeddings)
    {
        builder.Services.AddDatabricksEmbeddings();
        builder.Services.AddSqliteVectorStore(builder.Configuration);
        builder.Services.AddAiAgentCanvasRag();
    }
    else if (azureEmbeddings)
    {
        builder.Services.AddAzureAIFoundryEmbeddings();
        builder.Services.AddSqliteVectorStore(builder.Configuration);
        builder.Services.AddAiAgentCanvasRag();
    }
}

var app = builder.Build();

app.UseAiAgentCanvasSecurity();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAiAgentCanvas();
app.MapA2AEndpoints();
app.MapDevUI();
if (features.Notifications) app.MapNotificationEndpoints();
if (features.EventTriggers) app.MapEventTriggerEndpoints();
app.MapFallbackToFile("index.html");

app.Run();
