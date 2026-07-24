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
using AiAgentCanvas.Capabilities.SystemTools;
using AiAgentCanvas.Orchestration;
using AiAgentCanvas.Storage.Sqlite;
using AiAgentCanvas.Providers.AzureAIFoundry;
using AiAgentCanvas.Security;
using Agent.FinancialAnalyst;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using DataConnection.MarketData;
using DataConnection.VectorStore.Sqlite;
using Microsoft.Agents.AI.DevUI;

var builder = WebApplication.CreateBuilder(args);

if (!string.IsNullOrEmpty(builder.Configuration["ApplicationInsights:ConnectionString"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o =>
        o.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"]);
}

builder.Services.AddAzureAIFoundry(builder.Configuration);
builder.Services.AddAiAgentCanvasSecurity(builder.Configuration);
builder.Services.AddAiAgentCanvasPurview(builder.Configuration);
builder.Services.AddDevUI();

builder.Services.AddAiAgentCanvas(builder.Configuration, options =>
{
    options.AgentName = "AiAgentCanvas";
    options.AgentDescription = "A multi-tool AI assistant with market data, scheduling, skills, and MCP integration.";
});

builder.Services.AddFinancialAnalystAgent();
builder.Services.AddMarketDataTools();
builder.Services.AddAiAgentCanvasSystemTools(options =>
{
    options.AllowedCommands = ["dotnet", "git", "npm", "node"];
    options.ScriptTimeoutSeconds = 30;
});
builder.Services.AddAiAgentCanvasNotifications();
builder.Services.AddSqliteScheduledTaskStore();
builder.Services.AddAiAgentCanvasScheduler();
builder.Services.AddAiAgentCanvasSkills();
builder.Services.AddAiAgentCanvasMcp();
builder.Services.AddAiAgentCanvasPersonas();
builder.Services.AddAiAgentCanvasContext();
builder.Services.AddAiAgentCanvasWorkflows();
builder.Services.AddAiAgentCanvasEntities();
builder.Services.AddAiAgentCanvasUserProfiles();
builder.Services.AddAiAgentCanvasGuardrails();
builder.Services.AddAiAgentCanvasSkillRegistry();
builder.Services.AddAiAgentCanvasSkillAuthoring();

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

builder.Services.AddSqliteChatHistory();

if (!string.IsNullOrEmpty(builder.Configuration["AIFoundry:EmbeddingDeploymentName"]))
{
    builder.Services.AddAzureAIFoundryEmbeddings();
    builder.Services.AddSqliteVectorStore(builder.Configuration);
    builder.Services.AddAiAgentCanvasRag();
}

var app = builder.Build();

app.UseAiAgentCanvasSecurity();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAiAgentCanvas();
app.MapA2AEndpoints();
app.MapDevUI();
app.MapNotificationEndpoints();
app.MapFallbackToFile("index.html");

app.Run();
