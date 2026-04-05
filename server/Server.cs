
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NdoMcp.Server.Services;
using NdoMcp.Server.Handlers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AlertService>();
builder.Services.AddHttpClient<GemmaClient>();
builder.Services.AddHostedService<AlertGenerator>();

var app = builder.Build();
app.UseWebSockets();

app.Map("/mcp", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    var sessionId = Guid.NewGuid();
    Console.WriteLine($"[server] Client connected: {sessionId}");

    try
    {
        await McpHandler.Handle(ws, sessionId, context.RequestServices);
    }
    finally
    {
        Console.WriteLine($"[server] Client disconnected: {sessionId}");
        try { await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None); } catch { }
    }
});

await app.RunAsync("http://0.0.0.0:5000");
