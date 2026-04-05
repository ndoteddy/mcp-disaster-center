using FluentAssertions;
using Moq;
using NdoMcp.Server.Services;
using Xunit;

namespace NdoMcp.Server.Tests;

public class GemmaClientTests
{
    private readonly Mock<System.Net.Http.HttpClient> _mockHttpClient;
    private readonly GemmaClient _sut;

    public GemmaClientTests()
    {
        _mockHttpClient = new Mock<System.Net.Http.HttpClient>();
        _sut = new GemmaClient(_mockHttpClient.Object);
    }

    [Fact]
    public void Constructor_WithValidHttpClient_DoesNotThrow()
    {
        // Arrange
        var httpClient = new System.Net.Http.HttpClient();

        // Act
        var act = () => new GemmaClient(httpClient);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new GemmaClient(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Client_ReturnsProvidedHttpClient()
    {
        // Arrange
        var httpClient = new System.Net.Http.HttpClient();
        var client = new GemmaClient(httpClient);

        // Act
        var result = client.Client;

        // Assert
        result.Should().BeSameAs(httpClient);
    }

    [Fact]
    public void Client_ReturnsConsistentInstance()
    {
        // Act
        var client1 = _sut.Client;
        var client2 = _sut.Client;

        // Assert
        client1.Should().BeSameAs(client2);
    }
}
