using Microsoft.Toolkit.Uwp.Notifications;
using MizhbankNotifier.Services;

namespace MizhbankNotifier;

public class Worker : BackgroundService
{
    private readonly MizhbankService      _mizhbankService;
    private readonly BlackMarketService   _blackMarketService;
    private readonly TrayIconService      _trayIconService;
    private readonly RateStore            _rateStore;
    private readonly BlackMarketRateStore _blackMarketStore;
    private readonly ILogger<Worker>      _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    public Worker(MizhbankService mizhbankService, BlackMarketService blackMarketService,
        TrayIconService trayIconService, RateStore rateStore,
        BlackMarketRateStore blackMarketStore, ILogger<Worker> logger)
    {
        _mizhbankService    = mizhbankService;
        _blackMarketService = blackMarketService;
        _trayIconService    = trayIconService;
        _rateStore          = rateStore;
        _blackMarketStore   = blackMarketStore;
        _logger             = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Mizhbank notifier started");
        _trayIconService.Start();

        while (!stoppingToken.IsCancellationRequested)
        {
            // Fetch both markets concurrently
            var interbankTask   = _mizhbankService.GetChartDataAsync(stoppingToken);
            var blackMarketTask = _blackMarketService.GetChartDataAsync(stoppingToken);
            await Task.WhenAll(interbankTask, blackMarketTask);

            var chartData   = interbankTask.Result;
            var bmChartData = blackMarketTask.Result;

            if (chartData is { HasData: true })
            {
                _rateStore.Update(chartData);
                var latest = _rateStore.Latest;
                if (latest is not null)
                {
                    _trayIconService.UpdateTooltip(
                        $"USD/UAH К:{latest.Buy:F3} П:{latest.Sell:F3} ({latest.Time:HH:mm})");
                    ShowNotification(latest);
                }
            }

            if (bmChartData is { HasData: true })
                _blackMarketStore.Update(bmChartData);

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
