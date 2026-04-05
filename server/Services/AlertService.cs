using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;

namespace NdoMcp.Server.Services;

public class AlertService
{
    private readonly ConcurrentDictionary<string, (string type, int severity, string city, DateTime time)> _activeAlerts = new();
    private readonly ConcurrentDictionary<Guid, (WebSocket socket, HashSet<string> subs)> _sessions = new();

    public void AddSession(Guid id, WebSocket socket)
    {
        _sessions[id] = (socket, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public void RemoveSession(Guid id)
    {
        _sessions.TryRemove(id, out _);
    }

    public void SetInitialized(Guid id)
    {
        // No-op for now; presence in _sessions is enough
    }

    public void Subscribe(Guid id, string disasterType)
    {
        if (_sessions.TryGetValue(id, out var entry))
        {
            entry.subs.Add(disasterType);
        }
    }

    public string GetAlertsText()
    {
        var alertsList = new List<string>();
        if (_activeAlerts.IsEmpty)
        {
            alertsList.Add("No active alerts");
        }
        else
        {
            foreach (var alert in _activeAlerts.Values)
            {
                alertsList.Add($"[{alert.severity}/10] {alert.type.ToUpper()} in {alert.city} @ {alert.time:HH:mm:ss}");
            }
        }

        return string.Join("\n", alertsList);
    }

    public string AddAlert(string type, int severity, string city)
    {
        var id = Guid.NewGuid().ToString();
        _activeAlerts[id] = (type, severity, city, DateTime.UtcNow);

        // Broadcast to subscribed sessions
        var notification = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "notifications/alert",
            @params = new { alert_id = id, type, severity, city, timestamp = DateTime.UtcNow.ToString("O") }
        });

        foreach (var kv in _sessions)
        {
            var (socket, subs) = kv.Value;
            if (subs.Contains(type) && socket.State == WebSocketState.Open)
            {
                _ = socket.SendAsync(Encoding.UTF8.GetBytes(notification), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        return id;
    }

    public IReadOnlyDictionary<string, (string type, int severity, string city, DateTime time)> GetActiveAlertsSnapshot()
        => _activeAlerts.ToDictionary(kv => kv.Key, kv => kv.Value);

    public async Task<string> GenerateIncidentSummary(System.Net.Http.HttpClient httpClient)
    {
        var gemmaApiKey = Environment.GetEnvironmentVariable("GEMMA_API_KEY") ?? string.Empty;
        if (string.IsNullOrEmpty(gemmaApiKey))
        {
            return "ERROR: GEMMA_API_KEY environment variable not set. Set it and restart the server.";
        }

        if (_activeAlerts.IsEmpty)
        {
            return "No active incidents to analyze.";
        }

        var alertsSummary = new StringBuilder();
        var byType = _activeAlerts.Values.GroupBy(a => a.type);
        foreach (var group in byType)
        {
            var count = group.Count();
            var avgSeverity = group.Average(a => a.severity);
            var cities = string.Join(", ", group.Select(a => a.city).Distinct());
            alertsSummary.AppendLine($"- {group.Key}: {count} incidents (avg severity {avgSeverity:F1}/10) in {cities}");
        }

        var prompt = $@"You are a disaster command center AI assistant analyzing real-time incident data. \nProvide a brief (2-3 sentences) tactical assessment for incident commanders.\n\nACTIVE INCIDENTS:\n{alertsSummary}\nGive concise analysis and recommended priority actions.";

        try
        {
            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } }
            };

            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/gemma-4-31b-it:generateContent?key={gemmaApiKey}")
            {
                Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            var response = await httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"ERROR: Gemma API returned {response.StatusCode}: {content}";
            }

            var jsonDoc = JsonDocument.Parse(content);
            var candidates = jsonDoc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                var parts = first.GetProperty("content").GetProperty("parts");
                if (parts.GetArrayLength() > 0)
                {
                    return parts[0].GetProperty("text").GetString() ?? string.Empty;
                }
            }

            return "ERROR: Could not parse Gemma response";
        }
        catch (Exception ex)
        {
            return $"ERROR: LLM call failed: {ex.Message}";
        }
    }
}
