using Microsoft.Extensions.Hosting;

namespace NdoMcp.Server.Services;

/// <summary>
/// Background service that periodically generates simulated disaster alerts.
///
/// This hosted service produces randomized disaster events (earthquake, flood, storm)
/// and adds them to the central <see cref="AlertService"/> so connected clients can be
/// notified. It exists to provide a lightweight source of test data for the MCP protocol.
/// </summary>
public class AlertGenerator : BackgroundService
{
    private readonly AlertService _alertService;

    /// <summary>
/// Creates a new AlertGenerator bound to the application's <see cref="AlertService"/>.
///
/// The generator will call into <see cref="AlertService.AddAlert"/> to create alerts which are
/// then broadcast to subscribed sessions.
/// </summary>
/// <param name="alertService">The alert service used to store and broadcast generated alerts.</param>
public AlertGenerator(AlertService alertService)
    {
        _alertService = alertService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rnd = new Random();
        var disasterTypes = new[] { "earthquake", "flood", "storm" };
        var cities = new[] { "Coastville", "Hilltown" };

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(rnd.Next(2, 6)), stoppingToken);
            var dType = disasterTypes[rnd.Next(disasterTypes.Length)];
            var severity = rnd.Next(5, 11);
            var city = cities[rnd.Next(cities.Length)];
            var id = _alertService.AddAlert(dType, severity, city);
            Console.WriteLine($"[AlertGenerator] Generated alert: {dType} in {city} (sev={severity}) id={id}");
        }
    }
}
