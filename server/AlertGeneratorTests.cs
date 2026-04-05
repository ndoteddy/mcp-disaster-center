using FluentAssertions;
using Moq;
using NdoMcp.Server.Services;
using Xunit;

namespace NdoMcp.Server.Tests;

public class AlertGeneratorTests
{
    private readonly Mock<AlertService> _mockAlertService;
    private readonly AlertGenerator _sut;

    public AlertGeneratorTests()
    {
        _mockAlertService = new Mock<AlertService>();
        _sut = new AlertGenerator(_mockAlertService.Object);
    }

    [Fact]
    public void Constructor_WithValidAlertService_DoesNotThrow()
    {
        // Arrange
        var alertService = new AlertService();

        // Act
        var act = () => new AlertGenerator(alertService);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithNullAlertService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new AlertGenerator(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_GeneratesAlertsUntilCancelled()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100)); // Stop after 100ms

        // Act
        var task = _sut.StartAsync(cts.Token);
        await Task.Delay(150, CancellationToken.None); // Give it time to generate alerts
        await _sut.StopAsync(cts.Token);

        // Assert
        _mockAlertService.Verify(
            s => s.AddAlert(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_CallsAddAlertWithValidDisasterTypes()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var capturedDisasterTypes = new HashSet<string>();
        _mockAlertService.Setup(s => s.AddAlert(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((type, _, _) => capturedDisasterTypes.Add(type));

        // Act
        var task = _sut.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None);
        await _sut.StopAsync(cts.Token);

        // Assert
        var validTypes = new[] { "earthquake", "flood", "storm" };
        foreach (var type in capturedDisasterTypes)
        {
            validTypes.Should().Contain(type);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CallsAddAlertWithValidSeverity()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var capturedSeverities = new List<int>();
        _mockAlertService.Setup(s => s.AddAlert(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((_, severity, _) => capturedSeverities.Add(severity));

        // Act
        var task = _sut.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None);
        await _sut.StopAsync(cts.Token);

        // Assert
        foreach (var severity in capturedSeverities)
        {
            severity.Should().BeGreaterThanOrEqualTo(5).And.BeLessThanOrEqualTo(10);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CallsAddAlertWithValidCities()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var capturedCities = new HashSet<string>();
        _mockAlertService.Setup(s => s.AddAlert(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((_, _, city) => capturedCities.Add(city));

        // Act
        var task = _sut.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None);
        await _sut.StopAsync(cts.Token);

        // Assert
        var validCities = new[] { "Coastville", "Hilltown" };
        foreach (var city in capturedCities)
        {
            validCities.Should().Contain(city);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RespectsCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        var initialCallCount = 0;

        _mockAlertService.Setup(s => s.AddAlert(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((_, _, _) => Interlocked.Increment(ref initialCallCount));

        // Act
        var task = _sut.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None);
        await _sut.StopAsync(cts.Token);
        var finalCallCount = initialCallCount;

        // Wait a bit more and verify no more calls
        await Task.Delay(100, CancellationToken.None);

        // Assert - call count should remain stable after cancellation
        var newCallCount = _mockAlertService.Invocations.Count;
        newCallCount.Should().Be(finalCallCount);
    }
}
