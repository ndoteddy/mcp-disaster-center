using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NdoMcp.Server.Services;

namespace NdoMcp.Server.Handlers;

internal static class McpHandler
{
    private static readonly JsonElement AvailableTools;

    static McpHandler()
    {
        var toolsJson = JsonSerializer.Serialize(new object[]
        {
            new
            {
                name = "subscribe_disaster",
                description = "Subscribe to disaster alerts of a specific type (earthquake, flood, or storm)",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        type = new { type = "string", @enum = new[] { "earthquake", "flood", "storm" } }
                    },
                    required = new[] { "type" }
                }
            },
            new
            {
                name = "get_alerts",
                description = "Get current active disaster alerts",
                inputSchema = new { type = "object", properties = new { }, required = new string[] { } }
            },
            new
            {
                name = "incident_summary",
                description = "AI-powered analysis of active incidents using Gemma LLM for command center insights",
                inputSchema = new { type = "object", properties = new { }, required = new string[] { } }
            }
        });
        AvailableTools = JsonDocument.Parse(toolsJson).RootElement;
    }

    public static async Task Handle(WebSocket ws, Guid sessionId, IServiceProvider services)
    {
        var alertService = services.GetRequiredService<AlertService>();
        var gemmaClient = services.GetRequiredService<GemmaClient>();

        alertService.AddSession(sessionId, ws);

        var buffer = new byte[4 * 1024];

        while (ws.State == WebSocketState.Open)
        {
            var ms = new MemoryStream();
            WebSocketReceiveResult? res;
            try
            {
                do
                {
                    res = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (res.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, res.Count);
                } while (!res.EndOfMessage);
            }
            catch { break; }

            var txt = Encoding.UTF8.GetString(ms.ToArray());
            try
            {
                var doc = JsonDocument.Parse(txt);
                var root = doc.RootElement;

                if (root.TryGetProperty("method", out var methodEl))
                {
                    var method = methodEl.GetString();
                    var id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : (long?)null;

                    switch (method)
                    {
                        case "initialize":
                            alertService.SetInitialized(sessionId);
                            var response = JsonSerializer.Serialize(new
                            {
                                jsonrpc = "2.0",
                                id = id,
                                result = new
                                {
                                    protocolVersion = "2025-06-18",
                                    capabilities = new { tools = new { listChanged = true } },
                                    serverInfo = new { name = "disaster-alerts-mcp", version = "1.0.0" }
                                }
                            });
                            await SendMessage(ws, response);
                            break;

                        case "tools/list":
                            var toolsResponse = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id, result = new { tools = AvailableTools } });
                            await SendMessage(ws, toolsResponse);
                            break;

                        case "tools/call":
                            var paramsEl = root.GetProperty("params");
                            var toolName = paramsEl.GetProperty("name").GetString();
                            var argsEl = paramsEl.GetProperty("arguments");

                            var result = await ExecuteTool(toolName, argsEl, sessionId, alertService, gemmaClient.Client);
                            var toolCallResponse = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id, result = result });
                            await SendMessage(ws, toolCallResponse);
                            break;

                        default:
                            var errorResponse = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id, error = new { code = -32601, message = "Method not found" } });
                            await SendMessage(ws, errorResponse);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[McpHandler] Error: {ex.Message}");
            }
        }

        alertService.RemoveSession(sessionId);
    }

    static async Task<object> ExecuteTool(string toolName, JsonElement argsEl, Guid sessionId, AlertService alertService, System.Net.Http.HttpClient httpClient)
    {
        switch (toolName)
        {
            case "subscribe_disaster":
            {
                var disasterType = argsEl.GetProperty("type").GetString() ?? string.Empty;
                alertService.Subscribe(sessionId, disasterType);
                return new { content = new[] { new { type = "text", text = $"Subscribed to {disasterType} alerts" } } };
            }

            case "get_alerts":
            {
                var text = alertService.GetAlertsText();
                return new { content = new[] { new { type = "text", text } } };
            }

            case "incident_summary":
            {
                var summary = await alertService.GenerateIncidentSummary(httpClient);
                return new { content = new[] { new { type = "text", text = summary } } };
            }

            default:
                return new { content = new[] { new { type = "text", text = "Tool not found" } } };
        }
    }

    static Task SendMessage(WebSocket ws, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
