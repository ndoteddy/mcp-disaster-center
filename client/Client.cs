using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Net.Http;

// MCP (Model Context Protocol) Client - AI Agent with LLM-Powered Tool Orchestration
// This client acts like an INTELLIGENT AI agent:
// 1. Discovers available tools from the MCP server
// 2. Accepts natural language input from users
// 3. Uses Gemma LLM to decide which tool(s) to invoke
// 4. Executes tools and displays results
// 5. Learns and adapts based on user interactions

var uri = new Uri("ws://localhost:5000/mcp");
using var ws = new ClientWebSocket();
using var httpClient = new HttpClient();

// Placeholder: Set GEMMA_API_KEY environment variable before running
var gemmaApiKey = Environment.GetEnvironmentVariable("GEMMA_API_KEY") ?? "";
Console.WriteLine($"🔑 Gemma API Key Status: {(string.IsNullOrEmpty(gemmaApiKey) ? "⚠️ NOT SET (set GEMMA_API_KEY env var)" : "✅ SET")}\n");

Console.WriteLine($"🤖 Connecting to MCP server at {uri}...");
try
{
    await ws.ConnectAsync(uri, CancellationToken.None);
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Connection failed: {ex.Message}");
    return;
}

// Initialize MCP session
var initRequest = JsonSerializer.Serialize(new
{
    jsonrpc = "2.0",
    id = 1L,
    method = "initialize",
    @params = new
    {
        protocolVersion = "2025-06-18",
        capabilities = new { },
        clientInfo = new { name = "ai-agent-client", version = "1.0.0" }
    }
});
await ws.SendAsync(Encoding.UTF8.GetBytes(initRequest), WebSocketMessageType.Text, true, CancellationToken.None);

// Receive initialization response
var initResp = await ReceiveMessage(ws);
Console.WriteLine($"✅ Server initialized");

// Discover available tools from the server
Console.WriteLine("\n🔍 Discovering tools from MCP server...");
var toolsRequest = JsonSerializer.Serialize(new
{
    jsonrpc = "2.0",
    id = 2L,
    method = "tools/list",
    @params = new { }
});
await ws.SendAsync(Encoding.UTF8.GetBytes(toolsRequest), WebSocketMessageType.Text, true, CancellationToken.None);

var toolsResp = await ReceiveMessage(ws);
var availableTools = ParseToolsResponse(toolsResp);

Console.WriteLine($"\n📋 Available Tools ({availableTools.Count}):");
foreach (var tool in availableTools)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  • {tool["name"]} - {tool["description"]}");
    Console.ResetColor();
}

// Start background receive loop for notifications and responses
_ = Task.Run(async () => await ReceiveLoop(ws));

Console.WriteLine("\n💡 You can now type natural language commands:");
Console.WriteLine("  'Subscribe to earthquake alerts'");
Console.WriteLine("  'Show me active incidents'");
Console.WriteLine("  'Analyze the current disaster situation'");
Console.WriteLine("  Or use /commands: /help, /tools, /quit\n");

var messageId = 3L;
var toolDescriptions = string.Join("\n", availableTools.Select(t => $"- {t["name"]}: {t["description"]}"));

// Command loop
while (true)
{
    Console.Write("\n🧠 You: ");
    var line = Console.ReadLine();
    if (line is null) break;
    if (string.IsNullOrWhiteSpace(line)) continue;
    if (line.Equals("/quit", StringComparison.OrdinalIgnoreCase)) break;

    // Handle explicit commands
    if (line.StartsWith('/'))
    {
        await HandleCommand(line, availableTools);
        continue;
    }

    // Natural language processing via LLM - AI Agent Reasoning
    Console.WriteLine($"🤖 Agent: Thinking...");
    
    // If LLM is configured, use it for intelligent understanding and tool selection
    if (!string.IsNullOrEmpty(gemmaApiKey))
    {
        var reasoning = await ReasonAboutRequest(line, gemmaApiKey, httpClient);
        if (!string.IsNullOrEmpty(reasoning))
        {
            // Show the agent's understanding with streaming effect
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("💭 Agent Reasoning: ");
            await StreamText(reasoning, delayMs: 15);  // 15ms per character for natural flow
            Console.ResetColor();
            Console.WriteLine();
        }
        
        var toolSelection = await SelectToolWithLLM(line, toolDescriptions, gemmaApiKey, httpClient);
        if (toolSelection.HasValue)
        {
            var tool = toolSelection.Value;
            Console.WriteLine($"🔧 Selected tool: {tool.ToolName}");
            await InvokeTool(ws, messageId++, tool.ToolName, tool.Arguments);
        }
        else
        {
            // Fallback to keyword matching if LLM fails
            await HandleNaturalLanguageFallback(line, ws, messageId++, availableTools);
            messageId += 2;
        }
    }
    else
    {
        // Fallback: simple keyword matching without LLM
        await HandleNaturalLanguageFallback(line, ws, messageId++, availableTools);
        messageId += 2;
    }
}

Console.WriteLine("\n👋 Closing connection...");
try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", CancellationToken.None); } catch { }

static async Task HandleCommand(string line, List<Dictionary<string, string>> availableTools)
{
    var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var cmd = parts[0].Substring(1).ToLowerInvariant();
    var arg = parts.Length > 1 ? parts[1] : string.Empty;

    switch (cmd)
    {
        case "help":
            Console.WriteLine("\n📖 Commands:");
            Console.WriteLine("  /subscribe <type>       - Subscribe to alerts (earthquake, flood, storm)");
            Console.WriteLine("  /get-alerts             - Get current active alerts");
            Console.WriteLine("  /incident-summary       - AI analysis of active incidents");
            Console.WriteLine("  /tools                  - List available tools");
            Console.WriteLine("  /quit                   - Exit");
            break;

        case "tools":
            Console.WriteLine("\n📋 Available Tools:");
            foreach (var tool in availableTools)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  • {tool["name"]} - {tool["description"]}");
                Console.ResetColor();
            }
            break;

        default:
            Console.WriteLine("❓ Unknown command. Type /help");
            break;
    }

    await Task.CompletedTask;
}

// AI Agent Reasoning: Understand what the user is asking in human terms
static async Task<string> ReasonAboutRequest(string userInput, string gemmaApiKey, HttpClient httpClient)
{
    var prompt = $@"You are an AI assistant for a disaster alert command center. 

USER REQUEST: {userInput}

Think about what the user is asking for. Explain your understanding in 1-2 sentences.
What information or action are they seeking? 

Be concise and direct.";

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

        if (!response.IsSuccessStatusCode) return "";

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
                return text.Trim();
            }
        }

        return "";
    }
    catch
    {
        return "";
    }
}

// Stream text character-by-character for a typing effect (like ChatGPT)
static async Task StreamText(string text, int delayMs = 15)
{
    foreach (var c in text)
    {
        Console.Write(c);
        if (c != '\n' && c != ' ')  // Don't delay on newlines or spaces as much
        {
            await Task.Delay(delayMs);
        }
        else if (c == ' ')
        {
            await Task.Delay(Math.Max(1, delayMs / 3));  // Shorter delay for spaces
        }
    }
}

// LLM-powered tool selection: understands user intent and recommends the right tool
static async Task<(string ToolName, object Arguments)?> SelectToolWithLLM(string userInput, string toolDescriptions, 
    string gemmaApiKey, HttpClient httpClient)
{
    var prompt = $"STRICT INSTRUCTIONS: You MUST output ONLY a single line of valid JSON. No explanations before or after. No markdown. No asterisks. No extra text.\n\n" +
        $"AVAILABLE TOOLS:\n{toolDescriptions}\n\n" +
        $"USER REQUEST: {userInput}\n\n" +
        "RESPOND WITH ONLY THIS FORMAT (replace tool_name with the actual tool name):\n" +
        "{\"tool_name\":\"get_alerts\",\"arguments\":{}}";

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
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ LLM API error ({response.StatusCode}). Using keyword matching.");
            Console.ResetColor();
            return null;
        }

        // Parse Gemma response
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
                
                // Try to extract JSON from the response (in case LLM adds markdown)
                var jsonText = ExtractJSON(text);
                
                if (string.IsNullOrEmpty(jsonText))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"⚠️ No JSON found in LLM response: {text}");
                    Console.WriteLine($"   Using keyword matching.");
                    Console.ResetColor();
                    return null;
                }

                // Try to parse the JSON response from LLM
                try
                {
                    var toolJson = JsonDocument.Parse(jsonText);
                    var root = toolJson.RootElement;
                    
                    if (root.TryGetProperty("tool_name", out var toolNameEl) &&
                        root.TryGetProperty("arguments", out var argsEl))
                    {
                        var toolName = toolNameEl.GetString() ?? "";
                        var argsStr = argsEl.GetRawText();
                        var argsObj = JsonSerializer.Deserialize<object>(argsStr);
                        return (toolName, argsObj!);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"⚠️ LLM JSON missing required fields.");
                        Console.WriteLine($"   Extracted JSON: {jsonText}");
                        Console.WriteLine($"   Using keyword matching.");
                        Console.ResetColor();
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"⚠️ LLM JSON parse error: {ex.Message}");
                    Console.WriteLine($"   Raw JSON: {jsonText}");
                    Console.WriteLine($"   Using keyword matching.");
                    Console.ResetColor();
                    return null;
                }
            }
        }

        return null;
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"⚠️ LLM call failed: {ex.Message}. Using keyword matching.");
        Console.ResetColor();
        return null;
    }
}

// Extract JSON from text that may contain markdown or other content
static string ExtractJSON(string text)
{
    // First, try to find JSON starting with {"tool_name"
    var toolNameIdx = text.IndexOf("\"tool_name\"");
    if (toolNameIdx == -1)
        toolNameIdx = text.IndexOf("{", StringComparison.Ordinal);
    
    if (toolNameIdx == -1) return "";

    // Find the opening brace before tool_name
    var startIdx = text.LastIndexOf('{', toolNameIdx);
    if (startIdx == -1) return "";

    var braceCount = 0;
    var endIdx = -1;
    
    for (int i = startIdx; i < text.Length; i++)
    {
        if (text[i] == '{') braceCount++;
        else if (text[i] == '}')
        {
            braceCount--;
            if (braceCount == 0)
            {
                endIdx = i;
                break;
            }
        }
    }

    if (endIdx == -1 || endIdx <= startIdx) return "";
    return text.Substring(startIdx, endIdx - startIdx + 1);
}

// Fallback: Simple keyword matching when LLM is not available
static async Task HandleNaturalLanguageFallback(string userInput, ClientWebSocket ws, long messageId, 
    List<Dictionary<string, string>> availableTools)
{
    var input = userInput.ToLowerInvariant();

    if (input.Contains("subscribe") || input.Contains("subscribe to"))
    {
        var disasterType = "earthquake";
        if (input.Contains("flood")) disasterType = "flood";
        else if (input.Contains("storm")) disasterType = "storm";
        
        Console.WriteLine($"🔧 Selected tool: subscribe_disaster");
        await InvokeTool(ws, messageId, "subscribe_disaster", new { type = disasterType });
    }
    else if (input.Contains("alert") || input.Contains("incidents") || input.Contains("active"))
    {
        Console.WriteLine($"🔧 Selected tool: get_alerts");
        await InvokeTool(ws, messageId, "get_alerts", new { });
    }
    else if (input.Contains("analyze") || input.Contains("summary") || input.Contains("situation"))
    {
        Console.WriteLine($"🔧 Selected tool: incident_summary");
        await InvokeTool(ws, messageId, "incident_summary", new { });
    }
    else
    {
        Console.WriteLine($"❌ I didn't understand that. Try: 'subscribe to earthquakes', 'show alerts', or 'analyze situation'");
    }

    await Task.CompletedTask;
}

// Invoke a tool via MCP protocol
static async Task InvokeTool(ClientWebSocket ws, long messageId, string toolName, object arguments)
{
    var toolCall = JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0",
        id = messageId,
        method = "tools/call",
        @params = new
        {
            name = toolName,
            arguments = arguments
        }
    });
    
    await ws.SendAsync(Encoding.UTF8.GetBytes(toolCall), WebSocketMessageType.Text, true, CancellationToken.None);
}

static List<Dictionary<string, string>> ParseToolsResponse(string? json)
{
    var tools = new List<Dictionary<string, string>>();
    if (string.IsNullOrEmpty(json)) return tools;

    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("result", out var resultEl) && resultEl.TryGetProperty("tools", out var toolsEl))
        {
            foreach (var toolEl in toolsEl.EnumerateArray())
            {
                var tool = new Dictionary<string, string>();
                if (toolEl.TryGetProperty("name", out var nameEl)) tool["name"] = nameEl.GetString() ?? "";
                if (toolEl.TryGetProperty("description", out var descEl)) tool["description"] = descEl.GetString() ?? "";
                tools.Add(tool);
            }
        }
    }
    catch { }

    return tools;
}

static async Task ReceiveLoop(ClientWebSocket ws)
{
    while (ws.State == WebSocketState.Open)
    {
        try
        {
            var msg = await ReceiveMessage(ws);
            if (msg is null) return;

            using var doc = JsonDocument.Parse(msg);
            var root = doc.RootElement;

            // Handle JSON-RPC notifications (alerts) - check for "method" without "id"
            if (root.TryGetProperty("method", out var methodEl) && !root.TryGetProperty("id", out _))
            {
                var method = methodEl.GetString();
                if (method == "notifications/alert")
                {
                    // Try both "params" and "@params" key names
                    if (root.TryGetProperty("params", out var paramsEl) || root.TryGetProperty("@params", out paramsEl))
                    {
                        var alertType = "";
                        var severity = 0;
                        var city = "";

                        if (paramsEl.TryGetProperty("type", out var typeEl)) alertType = typeEl.GetString() ?? "";
                        if (paramsEl.TryGetProperty("severity", out var sevEl)) severity = sevEl.GetInt32();
                        if (paramsEl.TryGetProperty("city", out var cityEl)) city = cityEl.GetString() ?? "";

                        PrintAlert(severity, $"{alertType.ToUpper()} in {city}");
                    }
                }
            }

            // Handle JSON-RPC responses (results from tool calls)
            if (root.TryGetProperty("result", out var resultEl))
            {
                if (resultEl.TryGetProperty("content", out var contentEl))
                {
                    var content = contentEl.EnumerateArray().FirstOrDefault();
                    if (content.TryGetProperty("text", out var textEl))
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"\n✔️ ");
                        var resultText = textEl.GetString() ?? "";
                        // Stream the results for natural feel
                        _ = StreamText(resultText, delayMs: 8);
                        Console.ResetColor();
                        Console.WriteLine();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Don't crash on parse errors, just log and continue
            // Console.WriteLine($"⚠️ Receive error: {ex.Message}");
        }
    }
}

static async Task<string?> ReceiveMessage(ClientWebSocket ws)
{
    var buffer = new byte[8 * 1024];
    var ms = new MemoryStream();
    WebSocketReceiveResult? res;
    try
    {
        do
        {
            res = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (res.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, res.Count);
        } while (!res.EndOfMessage);
    }
    catch { return null; }

    return Encoding.UTF8.GetString(ms.ToArray());
}

static void PrintAlert(int severity, string text)
{
    // Map severity 5-10 to colors: yellow (5-7), red (8-10)
    if (severity <= 7) Console.ForegroundColor = ConsoleColor.Yellow;
    else Console.ForegroundColor = ConsoleColor.Red;

    Console.WriteLine($"\n🚨 ALERT [sev={severity}] {text}");
    Console.ResetColor();
}
