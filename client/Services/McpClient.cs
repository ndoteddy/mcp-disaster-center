using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace NdoMcp.Client.Services;

public record ToolDesc(string Name, string Description);

public class McpClient : IDisposable
{
    private readonly ClientWebSocket _ws = new();

    public async Task ConnectAsync(Uri uri)
    {
        await _ws.ConnectAsync(uri, CancellationToken.None);
    }

    public async Task InitializeAsync()
    {
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

        await SendAsync(initRequest);
        _ = await ReceiveMessage(); // discard init response
    }

    public async Task<List<ToolDesc>> ListToolsAsync()
    {
        var toolsRequest = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2L, method = "tools/list", @params = new { } });
        await SendAsync(toolsRequest);
        var resp = await ReceiveMessage();

        var list = new List<ToolDesc>();
        if (string.IsNullOrEmpty(resp)) return list;

        try
        {
            using var doc = JsonDocument.Parse(resp);
            var root = doc.RootElement;
            if (root.TryGetProperty("result", out var resultEl) && resultEl.TryGetProperty("tools", out var toolsEl))
            {
                foreach (var toolEl in toolsEl.EnumerateArray())
                {
                    var name = toolEl.GetProperty("name").GetString() ?? string.Empty;
                    var desc = toolEl.GetProperty("description").GetString() ?? string.Empty;
                    list.Add(new ToolDesc(name, desc));
                }
            }
        }
        catch { }

        return list;
    }

    public Task InvokeToolAsync(long messageId, string toolName, object arguments)
    {
        var toolCall = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = messageId, method = "tools/call", @params = new { name = toolName, arguments } });
        return SendAsync(toolCall);
    }

    public async Task StartReceiveLoop(Func<string, Task> onMessage)
    {
        while (_ws.State == WebSocketState.Open)
        {
            var msg = await ReceiveMessage();
            if (msg is null) break;
            await onMessage(msg);
        }
    }

    public async Task<string?> ReceiveMessage()
    {
        var buffer = new byte[8 * 1024];
        var ms = new MemoryStream();
        WebSocketReceiveResult? res;
        try
        {
            do
            {
                res = await _ws.ReceiveAsync(buffer, CancellationToken.None);
                if (res.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buffer, 0, res.Count);
            } while (!res.EndOfMessage);
        }
        catch { return null; }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public Task SendAsync(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public Task CloseAsync()
    {
        return _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", CancellationToken.None);
    }

    public void Dispose() => _ws?.Dispose();
}
