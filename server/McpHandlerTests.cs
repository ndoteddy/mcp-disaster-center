using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NdoMcp.Server.Handlers;
using NdoMcp.Server.Services;
using Xunit;

namespace NdoMcp.Server.Tests;

public class McpHandlerTests
{
    private readonly Mock<WebSocket> _mockWebSocket;
    private readonly Mock<AlertService> _mockAlertService;
    private readonly Mock<GemmaClient> _mockGemmaClient;
    private readonly Mock<System.Net.Http.HttpClient> _mockHttpClient;
    private readonly Mock<IServiceProvider> _mockServiceProvider;

    public McpHandlerTests()
    {
        _mockWebSocket = new Mock<WebSocket>();
        _mockAlertService = new Mock<AlertService>();
        _mockGemmaClient = new Mock<GemmaClient>();
        _mockHttpClient = new Mock<System.Net.Http.HttpClient>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        _mockGemmaClient.Setup(g => g.Client).Returns(_mockHttpClient.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(AlertService))).Returns(_mockAlertService.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(GemmaClient))).Returns(_mockGemmaClient.Object);
    }

    #region Helper Methods

    private void SetupWebSocketToReceiveMessage(string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var receivedOnce = false;

        _mockWebSocket.Setup(ws => ws.ReceiveAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ArraySegment<byte> buffer, CancellationToken _) =>
            {
                if (!receivedOnce)
                {
                    receivedOnce = true;
                    // Copy message to buffer
                    messageBytes.CopyTo(buffer.Array, buffer.Offset);
                    return new WebSocketReceiveResult(messageBytes.Length, WebSocketMessageType.Text, true);
                }
                else
                {
                    // Subsequent calls return close message
                    return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
                }
            });

        _mockWebSocket.Setup(ws => ws.State).Returns(WebSocketState.Open);
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    #endregion

    #region Initialize Message

    [Fact]
    public async Task Handle_WithInitializeMessage_RespondsWithCapabilities()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var initMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1L,
            method = "initialize",
            @params = new { }
        });

        SetupWebSocketToReceiveMessage(initMessage);
        var sentMessages = new List<ArraySegment<byte>>();
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken>((buffer, _, _, _) =>
            {
                sentMessages.Add(buffer);
            })
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        sentMessages.Should().HaveCount(1);
        var responseText = Encoding.UTF8.GetString(sentMessages[0]);
        var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("id").GetInt64().Should().Be(1L);
        root.TryGetProperty("result", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithInitializeMessage_IncludesServerInfo()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var initMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1L,
            method = "initialize"
        });

        SetupWebSocketToReceiveMessage(initMessage);
        var sentMessages = new List<ArraySegment<byte>>();
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken>((buffer, _, _, _) =>
            {
                sentMessages.Add(buffer);
            })
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        var responseText = Encoding.UTF8.GetString(sentMessages[0]);
        var doc = JsonDocument.Parse(responseText);
        var result = doc.RootElement.GetProperty("result");

        result.TryGetProperty("serverInfo", out var serverInfo).Should().BeTrue();
        serverInfo.GetProperty("name").GetString().Should().Be("disaster-alerts-mcp");
        serverInfo.GetProperty("version").GetString().Should().Be("1.0.0");
    }

    [Fact]
    public async Task Handle_WithInitializeMessage_CallsSetInitialized()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var initMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1L,
            method = "initialize"
        });

        SetupWebSocketToReceiveMessage(initMessage);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        _mockAlertService.Verify(s => s.SetInitialized(sessionId), Times.Once);
    }

    #endregion

    #region Tools List Message

    [Fact]
    public async Task Handle_WithToolsListMessage_RespondsWithAvailableTools()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var toolsMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2L,
            method = "tools/list"
        });

        SetupWebSocketToReceiveMessage(toolsMessage);
        var sentMessages = new List<ArraySegment<byte>>();
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken>((buffer, _, _, _) =>
            {
                sentMessages.Add(buffer);
            })
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        sentMessages.Should().HaveCount(1);
        var responseText = Encoding.UTF8.GetString(sentMessages[0]);
        var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("id").GetInt64().Should().Be(2L);
        root.TryGetProperty("result", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithToolsListMessage_IncludesExpectedTools()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var toolsMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2L,
            method = "tools/list"
        });

        SetupWebSocketToReceiveMessage(toolsMessage);
        var sentMessages = new List<ArraySegment<byte>>();
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken>((buffer, _, _, _) =>
            {
                sentMessages.Add(buffer);
            })
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        var responseText = Encoding.UTF8.GetString(sentMessages[0]);
        var doc = JsonDocument.Parse(responseText);
        var result = doc.RootElement.GetProperty("result");
        var tools = result.GetProperty("tools");

        tools.Should().NotBeNull();
    }

    #endregion

    #region Tool Call Messages

    [Fact]
    public async Task Handle_WithSubscribeDisasterToolCall_CallsSubscribe()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var toolCallMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 3L,
            method = "tools/call",
            @params = new
            {
                name = "subscribe_disaster",
                arguments = new { type = "earthquake" }
            }
        });

        SetupWebSocketToReceiveMessage(toolCallMessage);
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        _mockAlertService.Verify(s => s.Subscribe(sessionId, "earthquake"), Times.Once);
    }

    [Fact]
    public async Task Handle_WithSubscribeDisasterToolCall_RespondWithSuccess()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var toolCallMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 3L,
            method = "tools/call",
            @params = new
            {
                name = "subscribe_disaster",
                arguments = new { type = "earthquake" }
            }
        });

        SetupWebSocketToReceiveMessage(toolCallMessage);
        var sentMessages = new List<ArraySegment<byte>>();
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken>((buffer, _, _, _) =>
            {
                sentMessages.Add(buffer);
            })
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        sentMessages.Should().HaveCount(1);
        var responseText = Encoding.UTF8.GetString(sentMessages[0]);
        responseText.Should().Contain("Subscribed to earthquake alerts");
    }

    [Fact]
    public async Task Handle_WithGetAlertsToolCall_CallsGetAlertsText()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var toolCallMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 4L,
            method = "tools/call",
            @params = new
            {
                name = "get_alerts",
                arguments = new { }
            }
        });

        SetupWebSocketToReceiveMessage(toolCallMessage);
        _mockAlertService.Setup(s => s.GetAlertsText()).Returns("Test alerts");
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        _mockAlertService.Verify(s => s.GetAlertsText(), Times.Once);
    }

    [Fact]
    public async Task Handle_WithGetAlertsToolCall_ReturnAlertsInResponse()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var toolCallMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 4L,
            method = "tools/call",
            @params = new
            {
                name = "get_alerts",
                arguments = new { }
            }
        });

        SetupWebSocketToReceiveMessage(toolCallMessage);
        _mockAlertService.Setup(s => s.GetAlertsText()).Returns("Test alerts content");
        var sentMessages = new List<ArraySegment<byte>>();
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken>((buffer, _, _, _) =>
            {
                sentMessages.Add(buffer);
            })
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        var responseText = Encoding.UTF8.GetString(sentMessages[0]);
        responseText.Should().Contain("Test alerts content");
    }

    [Fact]
    public async Task Handle_WithIncidentSummaryToolCall_CallsGenerateIncidentSummary()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var toolCallMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 5L,
            method = "tools/call",
            @params = new
            {
                name = "incident_summary",
                arguments = new { }
            }
        });

        SetupWebSocketToReceiveMessage(toolCallMessage);
        _mockAlertService.Setup(s => s.GenerateIncidentSummary(It.IsAny<System.Net.Http.HttpClient>()))
            .ReturnsAsync("Summary");
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        _mockAlertService.Verify(
            s => s.GenerateIncidentSummary(It.IsAny<System.Net.Http.HttpClient>()),
            Times.Once);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Handle_WithUnknownMethod_ReturnsMethodNotFoundError()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var unknownMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 99L,
            method = "unknown/method"
        });

        SetupWebSocketToReceiveMessage(unknownMessage);
        var sentMessages = new List<ArraySegment<byte>>();
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken>((buffer, _, _, _) =>
            {
                sentMessages.Add(buffer);
            })
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        var responseText = Encoding.UTF8.GetString(sentMessages[0]);
        var doc = JsonDocument.Parse(responseText);
        var error = doc.RootElement.GetProperty("error");
        
        error.GetProperty("code").GetInt32().Should().Be(-32601);
        error.GetProperty("message").GetString().Should().Be("Method not found");
    }

    [Fact]
    public async Task Handle_WithUnknownTool_ReturnsToolNotFound()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var toolCallMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 6L,
            method = "tools/call",
            @params = new
            {
                name = "unknown_tool",
                arguments = new { }
            }
        });

        SetupWebSocketToReceiveMessage(toolCallMessage);
        var sentMessages = new List<ArraySegment<byte>>();
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken>((buffer, _, _, _) =>
            {
                sentMessages.Add(buffer);
            })
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        var responseText = Encoding.UTF8.GetString(sentMessages[0]);
        responseText.Should().Contain("Tool not found");
    }

    [Fact]
    public async Task Handle_CallsAddSessionOnStart()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var initMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1L,
            method = "initialize"
        });

        SetupWebSocketToReceiveMessage(initMessage);
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        _mockAlertService.Verify(s => s.AddSession(sessionId, _mockWebSocket.Object), Times.Once);
    }

    [Fact]
    public async Task Handle_CallsRemoveSessionOnEnd()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var initMessage = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1L,
            method = "initialize"
        });

        SetupWebSocketToReceiveMessage(initMessage);
        _mockWebSocket.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await McpHandler.Handle(_mockWebSocket.Object, sessionId, _mockServiceProvider.Object);

        // Assert
        _mockAlertService.Verify(s => s.RemoveSession(sessionId), Times.Once);
    }

    #endregion
}
