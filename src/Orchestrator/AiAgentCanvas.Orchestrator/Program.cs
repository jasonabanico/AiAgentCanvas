#pragma warning disable MEAI001

using AiAgentCanvas.Abstractions;
using AiAgentCanvas.AgentData;
using AiAgentCanvas.AgentData.Personas;
using AiAgentCanvas.Core;
using AiAgentCanvas.Core.Agents;
using AiAgentCanvas.Notifications;
using AiAgentCanvas.Scheduler;
using AiAgentCanvas.Security;
using AiAgentCanvas.Skills;
using AiAgentCanvas.SystemTools;
using HelloWorldAgent;
using MCP.HelloWorldData;
using Microsoft.Agents.AI.DevUI;
using VectorStore.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAiAgentCanvasSecurity(builder.Configuration);
builder.Services.AddAiAgentCanvasPurview(builder.Configuration);
builder.Services.AddDevUI();

builder.Services.AddAiAgentCanvas(builder.Configuration, options =>
{
    options.AgentName = "AiAgentCanvas";
    options.AgentDescription = "A multi-tool AI assistant with market data, scheduling, skills, and MCP integration.";
});

builder.Services.AddHelloWorldAgent();
builder.Services.AddMarketDataTools();
builder.Services.AddAiAgentCanvasSystemTools(options =>
{
    options.AllowedCommands = ["dotnet", "git", "npm", "node"];
    options.ScriptTimeoutSeconds = 30;
});
builder.Services.AddAiAgentCanvasNotifications();
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
