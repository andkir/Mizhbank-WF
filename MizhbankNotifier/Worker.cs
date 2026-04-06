using Microsoft.Toolkit.Uwp.Notifications;
using MizhbankNotifier.Services;

namespace MizhbankNotifier;

public class Worker : BackgroundService
{
    private readonly MizhbankService          _mizhbankService;
    private readonly EurMizhbankService       _eurMizhbankService;
    private readonly BlackMarketService       _blackMarketService;
    private readonly EurBlackMarketService    _eurBlackMarketService;
    private readonly BankRateWebViewFetcher   _bankWebView;
    private readonly BankRateHistoryService   _bankHistory;
    private readonly TrayIconService          _trayIconService;
    private readonly RateStore                _rateStore;
    private readonly EurRateStore             _eurRateStore;
    private readonly BlackMarketRateStore     _blackMarketStore;
    private readonly EurBlackMarketRateStore  _eurBlackMarketStore;
    private readonly ForexService              _forexService;
    private readonly ForexRateStore            _forexStore;
    private readonly FinanceUaService          _financeUaService;
    private readonly FinanceUaRateStore        _financeUaStore;
    private readonly BankRateStore            _bankRateStore;
    private readonly DailyRateDb             _dailyRateDb;
    private readonly ILogger<Worker>          _logger;
    private readonly SemaphoreSlim            _refreshSem = new(0, 1);
    private static readonly TimeSpan Interval     = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BankInterval = TimeSpan.FromMinutes(10);

    public void RequestRefresh()
    {
        try { _refreshSem.Release(); } catch (SemaphoreFullException) { }
    }

    public Worker(MizhbankService mizhbankService, EurMizhbankService eurMizhbankService,
        BlackMarketService blackMarketService, EurBlackMarketService eurBlackMarketService,
        ForexService forexService, ForexRateStore forexStore,
        FinanceUaService financeUaService, FinanceUaRateStore financeUaStore,
        BankRateWebViewFetcher bankWebView, BankRateHistoryService bankHistory,
        TrayIconService trayIconService, RateStore rateStore, EurRateStore eurRateStore,
        BlackMarketRateStore blackMarketStore, EurBlackMarketRateStore eurBlackMarketStore,
        BankRateStore bankRateStore, DailyRateDb dailyRateDb, ILogger<Worker> logger)
    {
        _mizhbankService       = mizhbankService;
        _eurMizhbankService    = eurMizhbankService;
        _blackMarketService    = blackMarketService;
        _eurBlackMarketService = eurBlackMarketService;
        _forexService          = forexService;
        _forexStore            = forexStore;
        _financeUaService      = financeUaService;
        _financeUaStore        = financeUaStore;
        _bankWebView           = bankWebView;
        _bankHistory           = bankHistory;
        _trayIconService       = trayIconService;
        _rateStore             = rateStore;
        _eurRateStore          = eurRateStore;
        _blackMarketStore      = blackMarketStore;
        _eurBlackMarketStore   = eurBlackMarketStore;
        _bankRateStore         = bankRateStore;
        _dailyRateDb           = dailyRateDb;
        _logger                = logger;
        _trayIconService.RefreshAction = RequestRefresh;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Mizhbank notifier started");

        // Seed bank-rate "previous" from yesterday's history so trend arrows
        // appear on the very first fetch without needing two poll cycles.
        var (yUsd, yEur) = _bankHistory.Load(DateTime.Today.AddDays(-1));
        _bankRateStore.SeedPreviousFromHistory(yUsd, yEur);

        _trayIconService.Start();

        // Bank rates run on their own loop: init WebView2, then fetch immediately,
        // then every 10 minutes. Completely independent of the interbank loop.
        _ = RunBankRatesAsync(stoppingToken);

        // Interbank + black-market + EUR interbank loop
        while (!stoppingToken.IsCancellationRequested)
        {
            var interbankTask      = _mizhbankService.GetChartDataAsync(stoppingToken);
            var eurInterbankTask   = _eurMizhbankService.GetChartDataAsync(stoppingToken);
            var blackMarketTask    = _blackMarketService.GetChartDataAsync(stoppingToken);
            var eurBlackMarketTask = _eurBlackMarketService.GetChartDataAsync(stoppingToken);
            var forexTask          = _forexService.GetChartDataAsync(stoppingToken);
            var financeUaTask      = _financeUaService.GetChartDataAsync(stoppingToken);
            await Task.WhenAll(interbankTask, eurInterbankTask, blackMarketTask, eurBlackMarketTask, forexTask, financeUaTask);

            var chartData       = interbankTask.Result;
            var eurChartData    = eurInterbankTask.Result;
            var bmChartData     = blackMarketTask.Result;
            var eurBmChartData  = eurBlackMarketTask.Result;
            var forexChartData  = forexTask.Result;
            var financeUaData   = financeUaTask.Result;

            var today = DateOnly.FromDateTime(DateTime.Today);

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
                if (chartData.CurrentSession.Count > 0)
                    _dailyRateDb.UpsertDayChart(today, "interbank_usd", chartData.CurrentSession);
            }

            if (eurChartData is { HasData: true })
            {
                _eurRateStore.Update(eurChartData);
                if (eurChartData.CurrentSession.Count > 0)
                    _dailyRateDb.UpsertDayChart(today, "interbank_eur", eurChartData.CurrentSession);
            }

            if (bmChartData is { HasData: true })
            {
                _blackMarketStore.Update(bmChartData);
                if (bmChartData.CurrentSession.Count > 0)
                    _dailyRateDb.UpsertDayChart(today, "blackmarket_usd", bmChartData.CurrentSession);
            }

            if (eurBmChartData is { HasData: true })
            {
                _eurBlackMarketStore.Update(eurBmChartData);
                if (eurBmChartData.CurrentSession.Count > 0)
                    _dailyRateDb.UpsertDayChart(today, "blackmarket_eur", eurBmChartData.CurrentSession);
            }

            if (forexChartData is { HasData: true })
            {
                _forexStore.Update(forexChartData);
                if (forexChartData.CurrentSession.Count > 0)
                    _dailyRateDb.UpsertDayChart(today, "forex_eurusd", forexChartData.CurrentSession);
            }

            if (financeUaData is { HasData: true })
            {
                _financeUaStore.Update(financeUaData);
                if (financeUaData.CurrentSession.Count > 0)
                    _dailyRateDb.UpsertDayChart(today, "interbank_usd_finua", financeUaData.CurrentSession);
            }

            // After 19:00 (market closed) persist today's closing snapshot.
            // INSERT OR IGNORE means repeated calls in the same day are no-ops.
            if (DateTime.Now.Hour >= 19)
                TrySaveTodaySnapshot();

            // Wait for next poll interval, or wake up early on manual refresh
            try { await _refreshSem.WaitAsync(Interval, stoppingToken); }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
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

            if (usdRates is null)
                _logger.LogWarning("Bank rates: USD fetch returned null");
            else if (usdRates.Count == 0)
                _logger.LogWarning("Bank rates: USD fetch returned empty list");

            if (eurRates is null)
                _logger.LogWarning("Bank rates: EUR fetch returned null");
            else if (eurRates.Count == 0)
                _logger.LogWarning("Bank rates: EUR fetch returned empty list");

            if (usdRates is { Count: > 0 }) _bankRateStore.UpdateUsd(usdRates);
            if (eurRates is { Count: > 0 }) _bankRateStore.UpdateEur(eurRates);

            if (usdRates is { Count: > 0 } || eurRates is { Count: > 0 })
            {
                _bankHistory.Save(DateTime.Today,
                    usdRates ?? (IReadOnlyList<Models.BankRate>)_bankRateStore.Usd,
                    eurRates ?? (IReadOnlyList<Models.BankRate>)_bankRateStore.Eur);

            }

            await Task.Delay(BankInterval, ct);
        }
    }

    private void TrySaveTodaySnapshot()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var ib    = _rateStore.Latest;
        var ibEur = _eurRateStore.Latest;
        var bm    = _blackMarketStore.Latest;
        var bmEur = _eurBlackMarketStore.Latest;

        if (ib    is not null) _dailyRateDb.TryInsert(today, "interbank_usd",   ib.Buy,    ib.Sell);
        if (ibEur is not null) _dailyRateDb.TryInsert(today, "interbank_eur",   ibEur.Buy, ibEur.Sell);
        if (bm    is not null) _dailyRateDb.TryInsert(today, "blackmarket_usd", bm.Buy,    bm.Sell);
        if (bmEur is not null) _dailyRateDb.TryInsert(today, "blackmarket_eur", bmEur.Buy, bmEur.Sell);

        var fx = _forexStore.Latest;
        if (fx is not null) _dailyRateDb.TryInsert(today, "forex_eurusd", fx.Buy, fx.Sell);
    }

    private void ShowNotification(Models.InterbankRate rate)
    {
        if (DateTime.Now.Hour >= 17) return;

        var prev = _rateStore.PreviousLatest;
        if (prev is not null && prev.Buy == rate.Buy && prev.Sell == rate.Sell)
            return;

        new ToastContentBuilder()
            .AddText("Міжбанк USD/UAH")
            .AddText($"Купівля: {rate.Buy:F3}  |  Продаж: {rate.Sell:F3}")
            .AddText($"Час: {rate.Time:HH:mm dd.MM.yyyy}")
            .Show();
    }

    public override Task StopAsync(CancellationToken cancellationToken)
        => base.StopAsync(cancellationToken);
}
