using Microsoft.Extensions.Hosting;

namespace NdoMcp.Server.Services;

public class AlertGenerator : BackgroundService
{
    private readonly AlertService _alertService;

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
