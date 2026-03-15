using Microsoft.Toolkit.Uwp.Notifications;
using MizhbankNotifier.Services;

namespace MizhbankNotifier;

public class Worker : BackgroundService
{
    private readonly MizhbankService          _mizhbankService;
    private readonly BlackMarketService       _blackMarketService;
    private readonly BankRateWebViewFetcher   _bankWebView;
    private readonly BankRateHistoryService   _bankHistory;
    private readonly TrayIconService          _trayIconService;
    private readonly RateStore                _rateStore;
    private readonly BlackMarketRateStore     _blackMarketStore;
    private readonly BankRateStore            _bankRateStore;
    private readonly ILogger<Worker>          _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    public Worker(MizhbankService mizhbankService, BlackMarketService blackMarketService,
        BankRateWebViewFetcher bankWebView, BankRateHistoryService bankHistory,
        TrayIconService trayIconService, RateStore rateStore,
        BlackMarketRateStore blackMarketStore, BankRateStore bankRateStore,
        ILogger<Worker> logger)
    {
        _mizhbankService    = mizhbankService;
        _blackMarketService = blackMarketService;
        _bankWebView        = bankWebView;
        _bankHistory        = bankHistory;
        _trayIconService    = trayIconService;
        _rateStore          = rateStore;
        _blackMarketStore   = blackMarketStore;
        _bankRateStore      = bankRateStore;
        _logger             = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Mizhbank notifier started");

        _trayIconService.Start();

        // Bank rates run on their own loop: init WebView2, then fetch immediately,
        // then every 10 minutes. Completely independent of the interbank loop.
        _ = RunBankRatesAsync(stoppingToken);

        // Interbank + black-market loop
        while (!stoppingToken.IsCancellationRequested)
        {
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

    private async Task RunBankRatesAsync(CancellationToken ct)
    {
        // Wait for WebView2 to warm up (CF challenge auto-solved by Chromium).
        await _bankWebView.InitAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            var usdRates = await _bankWebView.FetchRatesAsync(1, ct);
            var eurRates = await _bankWebView.FetchRatesAsync(2, ct);

            if (usdRates is { Count: > 0 }) _bankRateStore.UpdateUsd(usdRates);
            if (eurRates is { Count: > 0 }) _bankRateStore.UpdateEur(eurRates);

            if (usdRates is { Count: > 0 } || eurRates is { Count: > 0 })
            {
                _bankHistory.Save(DateTime.Today,
                    usdRates ?? (IReadOnlyList<Models.BankRate>)_bankRateStore.Usd,
                    eurRates ?? (IReadOnlyList<Models.BankRate>)_bankRateStore.Eur);

            }

            await Task.Delay(Interval, ct);
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
