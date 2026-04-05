using NdoMcp.Client.Services;

var uri = new Uri("ws://localhost:5000/mcp");

using var httpClient = new HttpClient();
using var gemma = new GemmaClient(httpClient);
using var mcp = new McpClient();

var gemmaApiKey = Environment.GetEnvironmentVariable("GEMMA_API_KEY") ?? string.Empty;
Console.WriteLine($"🔑 Gemma API Key Status: {(string.IsNullOrEmpty(gemmaApiKey) ? "⚠️ NOT SET (set GEMMA_API_KEY env var)" : "✅ SET")}\n");

Console.WriteLine($"🤖 Connecting to MCP server at {uri}...");
try
{
    await mcp.ConnectAsync(uri);
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Connection failed: {ex.Message}");
    return;
}

await mcp.InitializeAsync();
var availableTools = await mcp.ListToolsAsync();

Console.WriteLine($"\n📋 Available Tools ({availableTools.Count}):");
foreach (var tool in availableTools)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  • {tool.Name} - {tool.Description}");
    Console.ResetColor();
}

_ = Task.Run(async () => await mcp.StartReceiveLoop(async msg =>
{
    // Basic routing of incoming messages (notifications/results)
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(msg);
        var root = doc.RootElement;

        if (root.TryGetProperty("method", out var methodEl) && !root.TryGetProperty("id", out _))
        {
            var method = methodEl.GetString();
            if (method == "notifications/alert")
            {
                if (root.TryGetProperty("params", out var paramsEl) || root.TryGetProperty("@params", out paramsEl))
                {
                    var alertType = paramsEl.GetProperty("type").GetString() ?? string.Empty;
                    var severity = paramsEl.GetProperty("severity").GetInt32();
                    var city = paramsEl.GetProperty("city").GetString() ?? string.Empty;
                    PrintAlert(severity, $"{alertType.ToUpper()} in {city}");
                }
            }
        }

        if (root.TryGetProperty("result", out var resultEl) && resultEl.TryGetProperty("content", out var contentEl))
        {
            var content = contentEl.EnumerateArray().FirstOrDefault();
            if (content.TryGetProperty("text", out var textEl))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"\n✔️ ");
                _ = StreamText(textEl.GetString() ?? string.Empty, 8);
                Console.ResetColor();
                Console.WriteLine();
            }
        }
    }
    catch { }
}));

Console.WriteLine("\n💡 You can now type natural language commands:");
Console.WriteLine("  'Subscribe to earthquake alerts'");
Console.WriteLine("  'Show me active incidents'");
Console.WriteLine("  'Analyze the current disaster situation'");
Console.WriteLine("  Or use /commands: /help, /tools, /quit\n");

var messageId = 3L;
var toolDescriptions = string.Join("\n", availableTools.Select(t => $"- {t.Name}: {t.Description}"));

while (true)
{
    Console.Write("\n🧠 You: ");
    var line = Console.ReadLine();
    if (line is null) break;
    if (string.IsNullOrWhiteSpace(line)) continue;
    if (line.Equals("/quit", StringComparison.OrdinalIgnoreCase)) break;

    if (line.StartsWith('/'))
    {
        await HandleCommand(line, availableTools, mcp);
        continue;
    }

    Console.WriteLine($"🤖 Agent: Thinking...");

    if (!string.IsNullOrEmpty(gemmaApiKey))
    {
        var reasoning = await gemma.ReasonAboutRequest(line, gemmaApiKey);
        if (!string.IsNullOrEmpty(reasoning))
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("💭 Agent Reasoning: ");
            await StreamText(reasoning, delayMs: 15);
            Console.ResetColor();
            Console.WriteLine();
        }

        var toolSelection = await gemma.SelectToolWithLLM(line, toolDescriptions, gemmaApiKey);
        if (toolSelection.HasValue)
        {
            var tool = toolSelection.Value;
            Console.WriteLine($"🔧 Selected tool: {tool.ToolName}");
            await mcp.InvokeToolAsync(messageId++, tool.ToolName, tool.Arguments);
        }
        else
        {
            await HandleNaturalLanguageFallback(line, mcp, messageId++, availableTools);
            messageId += 2;
        }
    }
    else
    {
        await HandleNaturalLanguageFallback(line, mcp, messageId++, availableTools);
        messageId += 2;
    }
}

Console.WriteLine("\n👋 Closing connection...");
try { await mcp.CloseAsync(); } catch { }

static async Task HandleCommand(string line, List<ToolDesc> availableTools, McpClient mcp)
{
    var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var cmd = parts[0].Substring(1).ToLowerInvariant();

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
                Console.WriteLine($"  • {tool.Name} - {tool.Description}");
                Console.ResetColor();
            }
            break;

        default:
            Console.WriteLine("❓ Unknown command. Type /help");
            break;
    }

    await Task.CompletedTask;
}

static async Task HandleNaturalLanguageFallback(string userInput, McpClient mcp, long messageId, List<ToolDesc> availableTools)
{
    var input = userInput.ToLowerInvariant();

    if (input.Contains("subscribe") || input.Contains("subscribe to"))
    {
        var disasterType = "earthquake";
        if (input.Contains("flood")) disasterType = "flood";
        else if (input.Contains("storm")) disasterType = "storm";
        
        Console.WriteLine($"🔧 Selected tool: subscribe_disaster");
        await mcp.InvokeToolAsync(messageId, "subscribe_disaster", new { type = disasterType });
    }
    else if (input.Contains("alert") || input.Contains("incidents") || input.Contains("active"))
    {
        Console.WriteLine($"🔧 Selected tool: get_alerts");
        await mcp.InvokeToolAsync(messageId, "get_alerts", new { });
    }
    else if (input.Contains("analyze") || input.Contains("summary") || input.Contains("situation"))
    {
        Console.WriteLine($"🔧 Selected tool: incident_summary");
        await mcp.InvokeToolAsync(messageId, "incident_summary", new { });
    }
    else
    {
        Console.WriteLine($"❌ I didn't understand that. Try: 'subscribe to earthquakes', 'show alerts', or 'analyze situation'");
    }

    await Task.CompletedTask;
}

static async Task StreamText(string text, int delayMs = 15)
{
    foreach (var c in text)
    {
        Console.Write(c);
        if (c != '\n' && c != ' ')
        {
            await Task.Delay(delayMs);
        }
        else if (c == ' ')
        {
            await Task.Delay(Math.Max(1, delayMs / 3));
        }
    }
}

static void PrintAlert(int severity, string text)
{
    if (severity <= 7) Console.ForegroundColor = ConsoleColor.Yellow;
    else Console.ForegroundColor = ConsoleColor.Red;

    Console.WriteLine($"\n🚨 ALERT [sev={severity}] {text}");
    Console.ResetColor();
}

