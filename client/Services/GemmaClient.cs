using System.Net.Http;
using System.Text.Json;

namespace NdoMcp.Client.Services;

public class GemmaClient : IDisposable
{
    private readonly HttpClient _http;

    public GemmaClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> ReasonAboutRequest(string userInput, string gemmaApiKey)
    {
        var prompt = $@"You are an AI assistant for a disaster alert command center. \n\nUSER REQUEST: {userInput}\n\nThink about what the user is asking for. Explain your understanding in 1-2 sentences.\nWhat information or action are they seeking? \n\nBe concise and direct.";

        try
        {
            var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/gemma-4-31b-it:generateContent?key={gemmaApiKey}")
            { Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json") };

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return string.Empty;

            var jsonDoc = JsonDocument.Parse(content);
            var candidates = jsonDoc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                var parts = firstCandidate.GetProperty("content").GetProperty("parts");
                if (parts.GetArrayLength() > 0)
                {
                    return parts[0].GetProperty("text").GetString()?.Trim() ?? string.Empty;
                }
            }

            return string.Empty;
        }
        catch { return string.Empty; }
    }

    public async Task<(string ToolName, object Arguments)?> SelectToolWithLLM(string userInput, string toolDescriptions, string gemmaApiKey)
    {
        var prompt = "STRICT INSTRUCTIONS: You MUST output ONLY a single line of valid JSON. No explanations before or after. No markdown. No asterisks. No extra text.\n\n" +
            $"AVAILABLE TOOLS:\n{toolDescriptions}\n\nUSER REQUEST: {userInput}\n\n" +
            "RESPOND WITH ONLY THIS FORMAT (replace tool_name with the actual tool name):\n{" + "\"tool_name\":\"get_alerts\",\"arguments\":{}}";

        try
        {
            var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/gemma-4-31b-it:generateContent?key={gemmaApiKey}")
            { Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json") };

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return null;

            var jsonDoc = JsonDocument.Parse(content);
            var candidates = jsonDoc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                var parts = firstCandidate.GetProperty("content").GetProperty("parts");
                if (parts.GetArrayLength() > 0)
                {
                    var text = parts[0].GetProperty("text").GetString() ?? string.Empty;
                    var jsonText = ExtractJSON(text);
                    if (string.IsNullOrEmpty(jsonText)) return null;

                    try
                    {
                        var toolJson = JsonDocument.Parse(jsonText);
                        var root = toolJson.RootElement;
                        if (root.TryGetProperty("tool_name", out var toolNameEl) && root.TryGetProperty("arguments", out var argsEl))
                        {
                            var toolName = toolNameEl.GetString() ?? string.Empty;
                            var argsObj = JsonSerializer.Deserialize<object>(argsEl.GetRawText())!;
                            return (toolName, argsObj);
                        }
                    }
                    catch { return null; }
                }
            }

            return null;
        }
        catch { return null; }
    }

    static string ExtractJSON(string text)
    {
        var toolNameIdx = text.IndexOf("\"tool_name\"");
        if (toolNameIdx == -1) toolNameIdx = text.IndexOf("{", StringComparison.Ordinal);
        if (toolNameIdx == -1) return string.Empty;

        var startIdx = text.LastIndexOf('{', toolNameIdx);
        if (startIdx == -1) return string.Empty;

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
                    endIdx = i; break;
                }
            }
        }

        if (endIdx == -1 || endIdx <= startIdx) return string.Empty;
        return text.Substring(startIdx, endIdx - startIdx + 1);
    }

    public void Dispose() { }
}
