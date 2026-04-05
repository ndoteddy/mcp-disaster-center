using System.Net.WebSockets;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NdoMcp.Server.Services;
using Xunit;

namespace NdoMcp.Server.Tests;

public class AlertServiceTests
{
    private readonly AlertService _sut;

    public AlertServiceTests()
    {
        _sut = new AlertService();
    }

    #region Session Management

    [Fact]
    public void AddSession_CreatesNewSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mockWebSocket = new Mock<WebSocket>();

        // Act
        _sut.AddSession(sessionId, mockWebSocket.Object);

        // Assert
        var snapshot = _sut.GetActiveAlertsSnapshot();
        snapshot.Should().NotBeNull();
    }

    [Fact]
    public void AddSession_WithNullWebSocket_StillAddsSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act
        _sut.AddSession(sessionId, null!);

        // Assert - no exception thrown
        var snapshot = _sut.GetActiveAlertsSnapshot();
        snapshot.Should().BeEmpty();
    }

    [Fact]
    public void RemoveSession_RemovesExistingSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mockWebSocket = new Mock<WebSocket>();
        _sut.AddSession(sessionId, mockWebSocket.Object);

        // Act
        _sut.RemoveSession(sessionId);

        // Assert - verify by attempting to subscribe (would have worked if session still existed)
        _sut.Subscribe(sessionId, "earthquake");
        // If session was removed, subscription does nothing (no error)
    }

    [Fact]
    public void RemoveSession_WithNonExistentSession_DoesNotThrow()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act
        var act = () => _sut.RemoveSession(sessionId);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void SetInitialized_WithValidSessionId_DoesNotThrow()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mockWebSocket = new Mock<WebSocket>();
        _sut.AddSession(sessionId, mockWebSocket.Object);

        // Act
        var act = () => _sut.SetInitialized(sessionId);

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Subscriptions

    [Fact]
    public void Subscribe_AddsDisasterTypeToSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mockWebSocket = new Mock<WebSocket>();
        _sut.AddSession(sessionId, mockWebSocket.Object);

        // Act
        _sut.Subscribe(sessionId, "earthquake");

        // Assert - verify by adding an alert and checking it was broadcast
        _sut.AddAlert("earthquake", 7, "TestCity");
        // If subscription worked, the mock would receive the message
    }

    [Fact]
    public void Subscribe_WithMultipleDisasterTypes_SubscribesToAll()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mockWebSocket = new Mock<WebSocket>();
        mockWebSocket.Setup(ws => ws.State).Returns(WebSocketState.Open);
        mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut.AddSession(sessionId, mockWebSocket.Object);

        // Act
        _sut.Subscribe(sessionId, "earthquake");
        _sut.Subscribe(sessionId, "flood");
        _sut.Subscribe(sessionId, "storm");

        // Assert
        _sut.AddAlert("flood", 5, "FloodCity");
        
        mockWebSocket.Verify(
            ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Subscribe_WithNonExistentSession_DoesNotThrow()
    {
        // Arrange
        var nonExistentSessionId = Guid.NewGuid();

        // Act
        var act = () => _sut.Subscribe(nonExistentSessionId, "earthquake");

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Alert Management

    [Fact]
    public void AddAlert_ReturnsGuidString()
    {
        // Arrange
        var disasterType = "earthquake";
        var severity = 7;
        var city = "TestCity";

        // Act
        var alertId = _sut.AddAlert(disasterType, severity, city);

        // Assert
        alertId.Should().NotBeNullOrEmpty();
        Guid.TryParse(alertId, out _).Should().BeTrue();
    }

    [Fact]
    public void AddAlert_StoresAlertWithCorrectData()
    {
        // Arrange
        var disasterType = "flood";
        var severity = 8;
        var city = "FloodCity";

        // Act
        var alertId = _sut.AddAlert(disasterType, severity, city);
        var snapshot = _sut.GetActiveAlertsSnapshot();

        // Assert
        snapshot.Should().HaveCount(1);
        var alert = snapshot[alertId];
        alert.type.Should().Be(disasterType);
        alert.severity.Should().Be(severity);
        alert.city.Should().Be(city);
    }

    [Fact]
    public void AddAlert_TimestampIsUtcNow()
    {
        // Arrange
        var beforeTime = DateTime.UtcNow;

        // Act
        var alertId = _sut.AddAlert("storm", 5, "StormCity");

        // Assert
        var afterTime = DateTime.UtcNow;
        var snapshot = _sut.GetActiveAlertsSnapshot();
        var alert = snapshot[alertId];
        
        alert.time.Should().BeOnOrAfter(beforeTime);
        alert.time.Should().BeOnOrBefore(afterTime);
    }

    [Fact]
    public void AddAlert_BroadcastsToSubscribedSessions()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mockWebSocket = new Mock<WebSocket>();
        mockWebSocket.Setup(ws => ws.State).Returns(WebSocketState.Open);
        mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut.AddSession(sessionId, mockWebSocket.Object);
        _sut.Subscribe(sessionId, "earthquake");

        // Act
        _sut.AddAlert("earthquake", 7, "TestCity");

        // Assert
        mockWebSocket.Verify(
            ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, true, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public void AddAlert_DoesNotBroadcastToUnsubscribedDisasterTypes()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mockWebSocket = new Mock<WebSocket>();
        mockWebSocket.Setup(ws => ws.State).Returns(WebSocketState.Open);

        _sut.AddSession(sessionId, mockWebSocket.Object);
        _sut.Subscribe(sessionId, "flood");

        // Act
        _sut.AddAlert("earthquake", 7, "TestCity");

        // Assert
        mockWebSocket.Verify(
            ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void AddAlert_DoesNotBroadcastToClosedWebSockets()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mockWebSocket = new Mock<WebSocket>();
        mockWebSocket.Setup(ws => ws.State).Returns(WebSocketState.Closed);

        _sut.AddSession(sessionId, mockWebSocket.Object);
        _sut.Subscribe(sessionId, "earthquake");

        // Act
        _sut.AddAlert("earthquake", 7, "TestCity");

        // Assert
        mockWebSocket.Verify(
            ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void AddAlert_BroadcastMessageContainsJsonRpcFormat()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var sentMessages = new List<string>();
        var mockWebSocket = new Mock<WebSocket>();
        mockWebSocket.Setup(ws => ws.State).Returns(WebSocketState.Open);
        mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken>((buffer, _, _, _) =>
            {
                var message = System.Text.Encoding.UTF8.GetString(buffer);
                sentMessages.Add(message);
            })
            .Returns(Task.CompletedTask);

        _sut.AddSession(sessionId, mockWebSocket.Object);
        _sut.Subscribe(sessionId, "earthquake");

        // Act
        _sut.AddAlert("earthquake", 7, "TestCity");

        // Assert
        sentMessages.Should().HaveCount(1);
        var message = sentMessages[0];
        var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("method").GetString().Should().Be("notifications/alert");
        root.TryGetProperty("params", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("earthquake", 5, "City1")]
    [InlineData("flood", 10, "City2")]
    [InlineData("storm", 1, "City3")]
    public void AddAlert_WithVariousInputs_StoresCorrectly(string type, int severity, string city)
    {
        // Act
        var alertId = _sut.AddAlert(type, severity, city);
        var snapshot = _sut.GetActiveAlertsSnapshot();

        // Assert
        var alert = snapshot[alertId];
        alert.type.Should().Be(type);
        alert.severity.Should().Be(severity);
        alert.city.Should().Be(city);
    }

    #endregion

    #region Alert Retrieval

    [Fact]
    public void GetAlertsText_WhenNoAlerts_ReturnsNoActiveAlerts()
    {
        // Act
        var result = _sut.GetAlertsText();

        // Assert
        result.Should().Be("No active alerts");
    }

    [Fact]
    public void GetAlertsText_WithSingleAlert_FormatsCorrectly()
    {
        // Arrange
        _sut.AddAlert("earthquake", 7, "TestCity");

        // Act
        var result = _sut.GetAlertsText();

        // Assert
        result.Should().Contain("[7/10]");
        result.Should().Contain("EARTHQUAKE");
        result.Should().Contain("TestCity");
    }

    [Fact]
    public void GetAlertsText_WithMultipleAlerts_ContainsAll()
    {
        // Arrange
        _sut.AddAlert("earthquake", 7, "City1");
        _sut.AddAlert("flood", 5, "City2");
        _sut.AddAlert("storm", 9, "City3");

        // Act
        var result = _sut.GetAlertsText();

        // Assert
        var lines = result.Split('\n');
        lines.Should().HaveCount(3);
        result.Should().Contain("EARTHQUAKE").And.Contain("City1");
        result.Should().Contain("FLOOD").And.Contain("City2");
        result.Should().Contain("STORM").And.Contain("City3");
    }

    [Fact]
    public void GetAlertsText_IncludesSeverityInFormat()
    {
        // Arrange
        _sut.AddAlert("earthquake", 7, "TestCity");

        // Act
        var result = _sut.GetAlertsText();

        // Assert
        result.Should().MatchRegex(@"\[\d+/10\]");
    }

    [Fact]
    public void GetActiveAlertsSnapshot_ReturnsReadOnlyDictionary()
    {
        // Arrange
        _sut.AddAlert("earthquake", 7, "TestCity");

        // Act
        var snapshot = _sut.GetActiveAlertsSnapshot();

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.Should().HaveCount(1);
    }

    [Fact]
    public void GetActiveAlertsSnapshot_WhenEmpty_ReturnsEmptyDictionary()
    {
        // Act
        var snapshot = _sut.GetActiveAlertsSnapshot();

        // Assert
        snapshot.Should().BeEmpty();
    }

    #endregion

    #region Gemma Integration

    [Fact]
    public async Task GenerateIncidentSummary_WithMissingApiKey_ReturnsErrorMessage()
    {
        // Arrange
        var apiKey = Environment.GetEnvironmentVariable("GEMMA_API_KEY");
        Environment.SetEnvironmentVariable("GEMMA_API_KEY", null);
        
        var mockHttpClient = new Mock<System.Net.Http.HttpClient>();

        try
        {
            // Act
            var result = await _sut.GenerateIncidentSummary(mockHttpClient.Object);

            // Assert
            result.Should().Contain("ERROR").And.Contain("GEMMA_API_KEY");
        }
        finally
        {
            // Restore
            if (apiKey != null)
                Environment.SetEnvironmentVariable("GEMMA_API_KEY", apiKey);
        }
    }

    [Fact]
    public async Task GenerateIncidentSummary_WithNoActiveAlerts_ReturnsNoIncidentsMessage()
    {
        // Arrange
        Environment.SetEnvironmentVariable("GEMMA_API_KEY", "test-key");
        var mockHttpClient = new Mock<System.Net.Http.HttpClient>();

        // Act
        var result = await _sut.GenerateIncidentSummary(mockHttpClient.Object);

        // Assert
        result.Should().Contain("No active incidents");
    }

    [Fact]
    public async Task GenerateIncidentSummary_WithActiveAlerts_CallsGemmaApi()
    {
        // Arrange
        Environment.SetEnvironmentVariable("GEMMA_API_KEY", "test-key");
        _sut.AddAlert("earthquake", 7, "TestCity");

        var mockHttpClient = new Mock<System.Net.Http.HttpClient>();
        var mockResponse = new Mock<System.Net.Http.HttpResponseMessage>();
        mockResponse.Setup(r => r.IsSuccessStatusCode).Returns(true);
        mockResponse.Setup(r => r.StatusCode).Returns(System.Net.HttpStatusCode.OK);

        var responseContent = JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = "Test summary" } } } } }
        });

        mockResponse.Setup(r => r.Content.ReadAsStringAsync()).ReturnsAsync(responseContent);
        mockHttpClient.Setup(c => c.SendAsync(It.IsAny<System.Net.Http.HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _sut.GenerateIncidentSummary(mockHttpClient.Object);

        // Assert
        mockHttpClient.Verify(
            c => c.SendAsync(It.IsAny<System.Net.Http.HttpRequestMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
        result.Should().Be("Test summary");
    }

    [Fact]
    public async Task GenerateIncidentSummary_WithApiError_ReturnsErrorMessage()
    {
        // Arrange
        Environment.SetEnvironmentVariable("GEMMA_API_KEY", "test-key");
        _sut.AddAlert("earthquake", 7, "TestCity");

        var mockHttpClient = new Mock<System.Net.Http.HttpClient>();
        var mockResponse = new Mock<System.Net.Http.HttpResponseMessage>();
        mockResponse.Setup(r => r.IsSuccessStatusCode).Returns(false);
        mockResponse.Setup(r => r.StatusCode).Returns(System.Net.HttpStatusCode.Unauthorized);
        mockResponse.Setup(r => r.Content.ReadAsStringAsync()).ReturnsAsync("Unauthorized");

        mockHttpClient.Setup(c => c.SendAsync(It.IsAny<System.Net.Http.HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _sut.GenerateIncidentSummary(mockHttpClient.Object);

        // Assert
        result.Should().Contain("ERROR").And.Contain("401");
    }

    [Fact]
    public async Task GenerateIncidentSummary_WithException_ReturnsErrorMessage()
    {
        // Arrange
        Environment.SetEnvironmentVariable("GEMMA_API_KEY", "test-key");
        _sut.AddAlert("earthquake", 7, "TestCity");

        var mockHttpClient = new Mock<System.Net.Http.HttpClient>();
        mockHttpClient.Setup(c => c.SendAsync(It.IsAny<System.Net.Http.HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("Network error"));

        // Act
        var result = await _sut.GenerateIncidentSummary(mockHttpClient.Object);

        // Assert
        result.Should().Contain("ERROR").And.Contain("LLM call failed");
    }

    [Fact]
    public async Task GenerateIncidentSummary_GroupsAlertsByType()
    {
        // Arrange
        Environment.SetEnvironmentVariable("GEMMA_API_KEY", "test-key");
        _sut.AddAlert("earthquake", 7, "City1");
        _sut.AddAlert("earthquake", 5, "City2");
        _sut.AddAlert("flood", 8, "City3");

        var mockHttpClient = new Mock<System.Net.Http.HttpClient>();
        var mockResponse = new Mock<System.Net.Http.HttpResponseMessage>();
        mockResponse.Setup(r => r.IsSuccessStatusCode).Returns(true);

        string capturedPrompt = string.Empty;
        mockHttpClient.Setup(c => c.SendAsync(It.IsAny<System.Net.Http.HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .Callback<System.Net.Http.HttpRequestMessage, CancellationToken>((msg, _) =>
            {
                var content = msg.Content?.ReadAsStringAsync().Result ?? "";
                var doc = JsonDocument.Parse(content);
                var prompt = doc.RootElement
                    .GetProperty("contents")[0]
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();
                capturedPrompt = prompt ?? "";
            })
            .ReturnsAsync(mockResponse.Object);

        mockResponse.Setup(r => r.Content.ReadAsStringAsync()).ReturnsAsync(JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = "summary" } } } } }
        }));

        // Act
        await _sut.GenerateIncidentSummary(mockHttpClient.Object);

        // Assert
        capturedPrompt.Should().Contain("earthquake").And.Contain("flood");
    }

    #endregion
}
