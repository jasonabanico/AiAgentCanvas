using AiAgentCanvas.AgentData;
using AiAgentCanvas.Core;
using AiAgentCanvas.Notifications;
using AiAgentCanvas.Scheduler;
using AiAgentCanvas.Security;
using AiAgentCanvas.Skills;
using Hangfire;
using Hangfire.Storage.SQLite;
using MCP.MarketData;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAiAgentCanvasSecurity(builder.Configuration);

builder.Services.AddAiAgentCanvas(builder.Configuration, options =>
{
    options.AgentName = "AiAgentCanvas";
    options.AgentDescription = "A multi-tool AI assistant with market data, scheduling, skills, and MCP integration.";
});

builder.Services.AddMarketDataTools();
builder.Services.AddAiAgentCanvasNotifications();
builder.Services.AddAiAgentCanvasScheduler();
builder.Services.AddAiAgentCanvasSkills();
builder.Services.AddAiAgentCanvasMcp();
builder.Services.AddAiAgentCanvasPersonas();
builder.Services.AddAiAgentCanvasContext();
builder.Services.AddAiAgentCanvasSkillRegistry();
builder.Services.AddAiAgentCanvasSkillAuthoring();
builder.Services.AddAiAgentCanvasWorkflows();
builder.Services.AddAiAgentCanvasGuardrails();
builder.Services.AddAiAgentCanvasEntities();
builder.Services.AddAiAgentCanvasUserProfiles();

builder.Services.AddHangfire(config => config
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage("hangfire.db"));

builder.Services.AddHangfireServer();

var app = builder.Build();

app.UseAiAgentCanvasSecurity();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAiAgentCanvas();
app.MapNotificationEndpoints();
app.UseHangfireDashboard("/hangfire");
app.MapFallbackToFile("index.html");

app.Run();
