using Microsoft.Toolkit.Uwp.Notifications;
using MizhbankNotifier.Services;

namespace MizhbankNotifier;

public class Worker : BackgroundService
{
    private readonly MizhbankService _mizhbankService;
    private readonly TrayIconService _trayIconService;
    private readonly RateStore _rateStore;
    private readonly ILogger<Worker> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    public Worker(MizhbankService mizhbankService, TrayIconService trayIconService,
        RateStore rateStore, ILogger<Worker> logger)
    {
        _mizhbankService = mizhbankService;
        _trayIconService = trayIconService;
        _rateStore = rateStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Mizhbank notifier started");
        _trayIconService.Start();

        while (!stoppingToken.IsCancellationRequested)
        {
            var rates = await _mizhbankService.GetAllRatesAsync(stoppingToken);

            if (rates is { Count: > 0 })
            {
                _rateStore.Update(rates);
                var latest = rates[^1];
                _trayIconService.UpdateTooltip(
                    $"USD/UAH К:{latest.Buy:F3} П:{latest.Sell:F3} ({latest.Time:HH:mm})");
                ShowNotification(latest);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private void ShowNotification(Models.InterbankRate rate)
    {
        new ToastContentBuilder()
            .AddText("Міжбанк USD/UAH")
            .AddText($"Купівля: {rate.Buy:F3}  |  Продаж: {rate.Sell:F3}")
            .AddText($"Час: {rate.Time:HH:mm dd.MM.yyyy}")
            .Show();
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        ToastNotificationManagerCompat.Uninstall();
        return base.StopAsync(cancellationToken);
    }
}
