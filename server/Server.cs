
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Linq;

// MCP (Model Context Protocol) Server - .NET 10 Implementation
// Integrated with Google Gemma 4 LLM for disaster analysis

// JSON-RPC 2.0 message types
record JsonRpcRequest(string JsonRpc, long? Id, string Method, object? Params);
record JsonRpcResponse(string JsonRpc, long? Id, object? Result, object? Error);
record JsonRpcNotification(string JsonRpc, string Method, object? Params);

// MCP message structures
record InitializeParams(string ProtocolVersion, object Capabilities, ClientInfo ClientInfo);
record ClientInfo(string Name, string Version);
record ServerCapabilities(ToolsCapability Tools);
record ToolsCapability(bool ListChanged);
record Tool(string Name, string Description, object InputSchema);
record ToolResponse(Tool[] Tools);
record ContentBlock(string Type, string Text);
record ToolCallResponse(ContentBlock[] Content);

class Session
{
    public Guid Id { get; } = Guid.NewGuid();
    public WebSocket Socket { get; set; } = default!;
    public bool Initialized { get; set; }
    public HashSet<string> SubscribedDisasterTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
}

class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.UseWebSockets();

        var sessions = new ConcurrentDictionary<Guid, Session>();
        var messageIdMap = new ConcurrentDictionary<long, TaskCompletionSource<object>>();
        long messageIdCounter = 1;
        var activeAlerts = new ConcurrentDictionary<string, (string type, int severity, string city, DateTime time)>();
        var gemmaApiKey = Environment.GetEnvironmentVariable("GEMMA_API_KEY") ?? "";
        using var httpClient = new HttpClient();

        // Placeholder: Set GEMMA_API_KEY environment variable before running
        Console.WriteLine($"🔑 Gemma API Key Status: {(string.IsNullOrEmpty(gemmaApiKey) ? "⚠️ NOT SET (set GEMMA_API_KEY env var)" : "✅ SET")}");

        // Define available disaster alert tools
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
        var availableTools = JsonDocument.Parse(toolsJson).RootElement;

        // WebSocket MCP endpoint
        app.Map("/mcp", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            var ws = await context.WebSockets.AcceptWebSocketAsync();
            var session = new Session { Socket = ws };
            sessions[session.Id] = session;
            Console.WriteLine($"[server] Client connected: {session.Id}");

            try
            {
                await HandleMcpConnection(session, sessions, availableTools, activeAlerts, gemmaApiKey, httpClient);
            }
            finally
            {
                sessions.TryRemove(session.Id, out _);
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None); } catch { }
                Console.WriteLine($"[server] Client disconnected: {session.Id}");
            }
        });

        // Simulated disaster event generator - faster updates (every 2-5 seconds)
        _ = Task.Run(async () =>
        {
            var rnd = new Random();
            var disasterTypes = new[] { "earthquake", "flood", "storm" };
            var cities = new[] { "Coastville", "Hilltown" };

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(rnd.Next(2, 6))); // Faster: 2-5 seconds
                var dType = disasterTypes[rnd.Next(disasterTypes.Length)];
                var severity = rnd.Next(5, 11); // 5..10
                var city = cities[rnd.Next(cities.Length)];
                var id = Guid.NewGuid().ToString();

                // Store alert for incident_summary analysis
                activeAlerts[id] = (dType, severity, city, DateTime.UtcNow);

                Console.WriteLine($"[server] Generated alert: {dType} in {city} (sev={severity})");

                // Send notification to subscribed clients
                foreach (var sess in sessions.Values)
                {
                    if (sess.Initialized && sess.SubscribedDisasterTypes.Contains(dType))
                    {
                        var notification = JsonSerializer.Serialize(new
                        {
                            jsonrpc = "2.0",
                            method = "notifications/alert",
                            @params = new { alert_id = id, type = dType, severity, city, timestamp = DateTime.UtcNow.ToString("O") }
                        });
                        _ = SendMessage(sess.Socket, notification);
                    }
                }
            }
        });

        await app.RunAsync("http://0.0.0.0:5000");
    }

    static async Task HandleMcpConnection(Session session, ConcurrentDictionary<Guid, Session> sessions, JsonElement availableTools, 
        ConcurrentDictionary<string, (string type, int severity, string city, DateTime time)> activeAlerts, string gemmaApiKey, HttpClient httpClient)
    {
        var ws = session.Socket;
        var buffer = new byte[4 * 1024];
        long requestIdCounter = 0;

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
            catch { return; }

            var txt = Encoding.UTF8.GetString(ms.ToArray());
            try
            {
                var doc = JsonDocument.Parse(txt);
                var root = doc.RootElement;

                // Handle JSON-RPC 2.0 requests
                if (root.TryGetProperty("method", out var methodEl))
                {
                    var method = methodEl.GetString();
                    var id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : (long?)null;

                    switch (method)
                    {
                        case "initialize":
                            // MCP Initialize handshake
                            session.Initialized = true;
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
                            // List available tools
                            var toolsResponse = JsonSerializer.Serialize(new
                            {
                                jsonrpc = "2.0",
                                id = id,
                                result = new { tools = availableTools }
                            });
                            await SendMessage(ws, toolsResponse);
                            break;

                        case "tools/call":
                            // Execute a tool
                            var paramsEl = root.GetProperty("params");
                            var toolName = paramsEl.GetProperty("name").GetString();
                            var argsEl = paramsEl.GetProperty("arguments");

                            var result = await ExecuteTool(toolName, argsEl, session, activeAlerts, gemmaApiKey, httpClient);
                            var toolCallResponse = JsonSerializer.Serialize(new
                            {
                                jsonrpc = "2.0",
                                id = id,
                                result = result
                            });
                            await SendMessage(ws, toolCallResponse);
                            break;

                        default:
                            // Unknown method error
                            var errorResponse = JsonSerializer.Serialize(new
                            {
                                jsonrpc = "2.0",
                                id = id,
                                error = new { code = -32601, message = "Method not found" }
                            });
                            await SendMessage(ws, errorResponse);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[server] Error processing message: {ex.Message}");
            }
        }
    }

    static async Task<object> ExecuteTool(string toolName, JsonElement argsEl, Session session,
        ConcurrentDictionary<string, (string type, int severity, string city, DateTime time)> activeAlerts, 
        string gemmaApiKey, HttpClient httpClient)
    {
        switch (toolName)
        {
            case "subscribe_disaster":
                var disasterType = argsEl.GetProperty("type").GetString() ?? "";
                session.SubscribedDisasterTypes.Add(disasterType);
                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = $"Subscribed to {disasterType} alerts" }
                    }
                };

            case "get_alerts":
                var alertsList = new List<string>();
                if (activeAlerts.Count == 0)
                {
                    alertsList.Add("No active alerts");
                }
                else
                {
                    foreach (var alert in activeAlerts.Values)
                    {
                        alertsList.Add($"[{alert.severity}/10] {alert.type.ToUpper()} in {alert.city} @ {alert.time:HH:mm:ss}");
                    }
                }
                var alertsText = string.Join("\n", alertsList);
                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = alertsText }
                    }
                };

            case "incident_summary":
                // Call Gemma LLM for AI-powered analysis
                var summary = await CallGemmaLLM(activeAlerts, gemmaApiKey, httpClient);
                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = summary }
                    }
                };

            default:
                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = "Tool not found" }
                    }
                };
        }
    }

    static async Task<string> CallGemmaLLM(ConcurrentDictionary<string, (string type, int severity, string city, DateTime time)> activeAlerts, 
        string gemmaApiKey, HttpClient httpClient)
    {
        if (string.IsNullOrEmpty(gemmaApiKey))
        {
            return "ERROR: GEMMA_API_KEY environment variable not set. Set it and restart the server.";
        }

        if (activeAlerts.Count == 0)
        {
            return "No active incidents to analyze.";
        }

        // Format alert summary for LLM
        var alertsSummary = new StringBuilder();
        var byType = activeAlerts.Values.GroupBy(a => a.type);
        foreach (var group in byType)
        {
            var count = group.Count();
            var avgSeverity = group.Average(a => a.severity);
            var cities = string.Join(", ", group.Select(a => a.city).Distinct());
            alertsSummary.AppendLine($"- {group.Key}: {count} incidents (avg severity {avgSeverity:F1}/10) in {cities}");
        }

        var prompt = $@"You are a disaster command center AI assistant analyzing real-time incident data. 
Provide a brief (2-3 sentences) tactical assessment for incident commanders.

ACTIVE INCIDENTS:
{alertsSummary}

Give concise analysis and recommended priority actions.";

        try
        {
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[] { new { text = prompt } }
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, 
                $"https://generativelanguage.googleapis.com/v1beta/models/gemma-4-31b-it:generateContent?key={gemmaApiKey}")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            var response = await httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"ERROR: Gemma API returned {response.StatusCode}: {content}";
            }

            // Parse Gemma response: {candidates: [{content: {parts: [{text: "..."}]}}]}
            var jsonDoc = JsonDocument.Parse(content);
            var candidates = jsonDoc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                var contentObj = firstCandidate.GetProperty("content");
                var parts = contentObj.GetProperty("parts");
                if (parts.GetArrayLength() > 0)
                {
                    var text = parts[0].GetProperty("text").GetString() ?? "";
                    return text;
                }
            }

            return "ERROR: Could not parse Gemma response";
        }
        catch (Exception ex)
        {
            return $"ERROR: LLM call failed: {ex.Message}";
        }
    }

    static Task SendMessage(WebSocket ws, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    static Task SendJsonRpc(WebSocket ws, object msg)
    {
        var json = JsonSerializer.Serialize(msg);
        var bytes = Encoding.UTF8.GetBytes(json);
        return ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
