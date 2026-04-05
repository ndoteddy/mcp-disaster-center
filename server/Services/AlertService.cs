using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;

namespace NdoMcp.Server.Services;

/// <summary>
/// In-memory alert store and notification hub for WebSocket sessions.
///
/// Responsible for tracking active incidents, managing client subscriptions, and broadcasting
/// alert notifications to connected WebSocket sessions. This implementation is intentionally
/// simple and holds data in-memory; it is suitable for demos and local testing but not for
/// production scale or persistence requirements.
/// </summary>
public class AlertService
{
    private readonly ConcurrentDictionary<string, (string type, int severity, string city, DateTime time)> _activeAlerts = new();
    private readonly ConcurrentDictionary<Guid, (WebSocket socket, HashSet<string> subs)> _sessions = new();

    /// <summary>
/// Register a WebSocket session so the service can send notifications to the client.
///
/// Stores the socket and an initially-empty subscription list for the session id.
/// </summary>
/// <param name="id">Unique session identifier supplied by the server connection handler.</param>
/// <param name="socket">The open <see cref="WebSocket"/> through which notifications are sent.</param>
public void AddSession(Guid id, WebSocket socket)
    {
        _sessions[id] = (socket, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
/// Remove a previously-registered session and stop sending notifications to it.
/// </summary>
/// <param name="id">The session identifier to remove.</param>
public void RemoveSession(Guid id)
    {
        _sessions.TryRemove(id, out _);
    }

    /// <summary>
/// Mark a session as initialized. Currently a no-op; presence in the session registry is sufficient.
///
/// Kept as a semantic hook for clients that perform an explicit initialization handshake.
/// </summary>
/// <param name="id">Session identifier that completed initialization.</param>
public void SetInitialized(Guid id)
    {
        // No-op for now; presence in _sessions is enough
    }

    /// <summary>
/// Subscribe the specified session to alerts of the given disaster type.
///
/// Subscribed sessions will receive notification messages created by <see cref="AddAlert"/> when
/// an alert of the matching type is generated.
/// </summary>
/// <param name="id">Session identifier that is subscribing.</param>
/// <param name="disasterType">Disaster type (e.g. "earthquake", "flood", "storm").</param>
public void Subscribe(Guid id, string disasterType)
    {
        if (_sessions.TryGetValue(id, out var entry))
        {
            entry.subs.Add(disasterType);
        }
    }

    /// <summary>
/// Create a human-readable summary of currently active alerts.
///
/// Returns either a single line indicating no alerts or a newline-separated list of alerts with
/// severity, type, city and timestamp suitable for text responses over the MCP protocol.
/// </summary>
/// <returns>Newline-delimited textual summary of active alerts.</returns>
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

    /// <summary>
/// Add a new active alert and broadcast a notification to subscribed sessions.
///
/// This method stores the alert in-memory and asynchronously sends a JSON-RPC notification to
/// any connected WebSocket sessions that have subscribed to the alert type.
/// </summary>
/// <param name="type">Disaster type (e.g. "earthquake").</param>
/// <param name="severity">Severity on a 1-10 scale.</param>
/// <param name="city">City where the incident occurred.</param>
/// <returns>A string GUID that identifies the created alert.</returns>
/// <example>
/// <code>
/// var id = alertService.AddAlert("earthquake", 8, "Coastville");
/// </code>
/// </example>
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

    /// <summary>
/// Return a snapshot of active alerts suitable for read-only inspection.
///
/// The returned dictionary is a shallow copy of the in-memory store and may be iterated by callers
/// without affecting the service's internal collections.
/// </summary>
/// <returns>Read-only dictionary mapping alert id to alert details (type, severity, city, timestamp).</returns>
public IReadOnlyDictionary<string, (string type, int severity, string city, DateTime time)> GetActiveAlertsSnapshot()
        => _activeAlerts.ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
/// Generate a concise incident summary using the Gemma LLM for command-center analysis.
///
/// The method aggregates active alerts into a short prompt and calls the external Gemma API.
/// It expects the environment variable <c>GEMMA_API_KEY</c> to be set. Errors from the LLM call are
/// returned as error strings rather than thrown exceptions to simplify caller handling.
/// </summary>
/// <param name="httpClient">An <see cref="System.Net.Http.HttpClient"/> used to call the Gemma API.</param>
/// <returns>A short natural-language summary or an error message describing the failure.</returns>
/// <example>
/// <code>
/// var summary = await alertService.GenerateIncidentSummary(httpClient);
/// Console.WriteLine(summary);
/// </code>
/// </example>
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
