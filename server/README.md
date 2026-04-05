# Server (disaster-alerts-mcp)

A minimal WebSocket-based MCP (Message Control Protocol) server that simulates disaster alerts and exposes a small set of tools for clients: subscribing to alert types, retrieving current alerts, and requesting an AI-powered incident summary.

## Usage

// DI registration
builder.Services.AddSingleton<AlertService>();
builder.Services.AddHttpClient<GemmaClient>();
builder.Services.AddHostedService<AlertGenerator>();

// Basic runtime
app.UseWebSockets();
app.Map("/mcp", ...);

// Basic client usage (pseudo-code)
var ws = await ConnectWebSocket("ws://localhost:5000/mcp");
await SendJson(ws, new { method = "initialize", id = 1 });
await SendJson(ws, new { method = "tools/call", id = 2, @params = new { name = "subscribe_disaster", arguments = new { type = "earthquake" } } });

## API Reference

| Method | Description | Returns |
|--------|-------------|---------|
| AlertService.AddSession(id, socket) | Register a WebSocket session to receive notifications | void |
| AlertService.RemoveSession(id) | Unregister a session | void |
| AlertService.SetInitialized(id) | Mark session initialized (no-op presently) | void |
| AlertService.Subscribe(id, type) | Subscribe session to a disaster type | void |
| AlertService.GetAlertsText() | Human-readable summary of active alerts | string |
| AlertService.AddAlert(type,severity,city) | Add alert and broadcast to subscribers | string (alert id) |
| AlertService.GetActiveAlertsSnapshot() | Read-only snapshot of active alerts | IReadOnlyDictionary<string,(type,severity,city,time)> |
| AlertService.GenerateIncidentSummary(httpClient) | Calls Gemma LLM to produce a short tactical summary | Task<string> (summary or error) |
| GemmaClient.Client (property) | Exposes typed HttpClient for Gemma API calls | HttpClient |
| AlertGenerator (hosted service) | Background generator that adds simulated alerts | BackgroundService |

## Architecture Notes

- Uses in-memory concurrent collections (ConcurrentDictionary) for alerts and sessions — simple and fast for local testing but not persistent.
- WebSockets are used for real-time notifications; MCP-style JSON-RPC messages are implemented in the connection handler.
- Gemma LLM integration is performed via the HTTP API and expects the GEMMA_API_KEY environment variable.
- AlertGenerator is registered as a hosted background service to continuously add simulated incidents.

## Dependencies

| Package / Service | Why |
|-------------------|-----|
| Microsoft.Extensions.Hosting | BackgroundService and hosted service support |
| System.Net.Http (HttpClientFactory) | Typed client for calling Gemma API |
| Gemma / generativelanguage API | Produces incident summaries (requires API key) |

## Operational notes

- Set environment variable `GEMMA_API_KEY` before calling `GenerateIncidentSummary`.
- The server listens on `http://0.0.0.0:5000` by default (see top-level Program/Server.cs).

## Related files

- Handlers/McpHandler.cs — connection handler implementing the MCP protocol and tool dispatch (internal).
- Services/AlertService.cs — in-memory alert store and notification hub.
- Services/AlertGenerator.cs — hosted service that generates test alerts.
- Services/GemmaClient.cs — typed HttpClient wrapper for LLM calls.
